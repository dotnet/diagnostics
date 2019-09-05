// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS
{
    /// <summary>
    /// Helper code to hosting SOS under ClrMD
    /// </summary>
    public sealed class SOSHost
    {
        internal const int S_OK = DebugClient.S_OK;
        internal const int E_INVALIDARG = DebugClient.E_INVALIDARG;
        internal const int E_FAIL = DebugClient.E_FAIL;
        internal const int E_NOTIMPL = DebugClient.E_NOTIMPL;
        internal const int E_NOINTERFACE = DebugClient.E_NOINTERFACE;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SOSCommandDelegate(
            IntPtr ILLDBServices,
            [In, MarshalAs(UnmanagedType.LPStr)] string args);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SOSInitializeDelegate(
            [In, MarshalAs(UnmanagedType.Struct)] ref SOSNetCoreCallbacks callbacks,
            int callbacksSize,
            [In, MarshalAs(UnmanagedType.LPStr)] string tempDirectory,
            [In, MarshalAs(UnmanagedType.LPStr)] string dacFilePath,
            [In, MarshalAs(UnmanagedType.LPStr)] string dbiFilePath,
            bool symbolStoreEnabled);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate UIntPtr GetExpressionDelegate(
            [In, MarshalAs(UnmanagedType.LPStr)] string expression);

        private const string SOSInitialize = "SOSInitializeByHost";

        #region SOS.NETCore function delegates

        private delegate bool InitializeSymbolStoreDelegate(
            bool logging,
            bool msdl,
            bool symweb,
            string tempDirectory,
            string symbolServerPath,
            string symbolCachePath,
            string symbolDirectoryPath,
            string windowsSymbolPath);

        private delegate void DisplaySymbolStoreDelegate(
            SymbolReader.WriteLine writeLine);

        private delegate void DisableSymbolStoreDelegate();

        private delegate void LoadNativeSymbolsDelegate(
            SymbolReader.SymbolFileCallback callback,
            IntPtr parameter,
            string moduleFilePath,
            ulong address,
            int size,
            SymbolReader.ReadMemoryDelegate readMemory);

        private delegate IntPtr LoadSymbolsForModuleDelegate(
            string assemblyPath,
            bool isFileLayout,
            ulong loadedPeAddress,
            int loadedPeSize,
            ulong inMemoryPdbAddress,
            int inMemoryPdbSize,
            SymbolReader.ReadMemoryDelegate readMemory);

        private delegate void DisposeDelegate(IntPtr symbolReaderHandle);

        private delegate bool ResolveSequencePointDelegate(
            IntPtr symbolReaderHandle,
            string filePath,
            int lineNumber,
            out int methodToken,
            out int ilOffset);

        private delegate bool GetLineByILOffsetDelegate(
            IntPtr symbolReaderHandle,
            int methodToken,
            long ilOffset,
            out int lineNumber,
            out IntPtr fileName);

        private delegate bool GetLocalVariableNameDelegate(
            IntPtr symbolReaderHandle,
            int methodToken,
            int localIndex,
            out IntPtr localVarName);

        private delegate int GetMetadataLocatorDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string imagePath,
            uint imageTimestamp,
            uint imageSize,
            [MarshalAs(UnmanagedType.LPArray, SizeConst = 16)] byte[] mvid,
            uint mdRva,
            uint flags,
            uint bufferSize,
            IntPtr buffer,
            IntPtr dataSize);

        #endregion

        /// <summary>
        /// Pass to SOSInitializeByHost
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct SOSNetCoreCallbacks
        {
            public InitializeSymbolStoreDelegate InitializeSymbolStoreDelegate;
            public DisplaySymbolStoreDelegate DisplaySymbolStoreDelegate;
            public DisableSymbolStoreDelegate DisableSymbolStoreDelegate;
            public LoadNativeSymbolsDelegate LoadNativeSymbolsDelegate;
            public LoadSymbolsForModuleDelegate LoadSymbolsForModuleDelegate;
            public DisposeDelegate DisposeDelegate;
            public ResolveSequencePointDelegate ResolveSequencePointDelegate;
            public GetLineByILOffsetDelegate GetLineByILOffsetDelegate;
            public GetLocalVariableNameDelegate GetLocalVariableNameDelegate;
            public GetMetadataLocatorDelegate GetMetadataLocatorDelegate;
            public GetExpressionDelegate GetExpressionDelegate;
        }

        static SOSNetCoreCallbacks s_callbacks = new SOSNetCoreCallbacks {
            InitializeSymbolStoreDelegate = SymbolReader.InitializeSymbolStore,
            DisplaySymbolStoreDelegate = SymbolReader.DisplaySymbolStore,
            DisableSymbolStoreDelegate = SymbolReader.DisableSymbolStore,
            LoadNativeSymbolsDelegate = SymbolReader.LoadNativeSymbols,
            LoadSymbolsForModuleDelegate = SymbolReader.LoadSymbolsForModule,
            DisposeDelegate = SymbolReader.Dispose,
            ResolveSequencePointDelegate = SymbolReader.ResolveSequencePoint,
            GetLineByILOffsetDelegate = SymbolReader.GetLineByILOffset,
            GetLocalVariableNameDelegate = SymbolReader.GetLocalVariableName,
            GetMetadataLocatorDelegate = MetadataHelper.GetMetadataLocator,
            GetExpressionDelegate = SOSHost.GetExpression,
        };

        internal readonly IDataReader DataReader;

        private static readonly string s_coreclrModuleName;

        private readonly AnalyzeContext _analyzeContext;
        private readonly RegisterService _registerService;
        private readonly IConsoleService _console;
        private readonly COMCallableIUnknown _ccw;  
        private readonly IntPtr _interface;
        private IntPtr _sosLibrary = IntPtr.Zero;
        private Dictionary<string, PEReader> _pathToPeReader = new Dictionary<string, PEReader>();

        /// <summary>
        /// Enable the assembly resolver to get the right SOS.NETCore version (the one
        /// in the same directory as SOS.Hosting).
        /// </summary>
        static SOSHost()
        {
            AssemblyResolver.Enable();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                s_coreclrModuleName = "coreclr";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                s_coreclrModuleName = "libcoreclr.so";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                s_coreclrModuleName = "libcoreclr.dylib";
            }
        }

        /// <summary>
        /// The native SOS binaries path. Default is OS/architecture (RID) named directory in the same directory as this assembly.
        /// </summary>
        public string SOSPath { get; set; }

        /// <summary>
        /// Create an instance of the hosting class
        /// </summary>
        /// <param name="serviceProvider">Service provider</param>
        public SOSHost(IServiceProvider serviceProvider)
        {
            DataTarget dataTarget = serviceProvider.GetService<DataTarget>();
            DataReader = dataTarget.DataReader;
            _console = serviceProvider.GetService<IConsoleService>();
            _analyzeContext = serviceProvider.GetService<AnalyzeContext>();
            _registerService = serviceProvider.GetService<RegisterService>();

            string rid = InstallHelper.GetRid();
            SOSPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), rid);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var debugClient = new DebugClient(this);
                _ccw = debugClient;
                _interface = debugClient.IDebugClient;
            }
            else
            {
                var lldbServices = new LLDBServices(this);
                _ccw = lldbServices;
                _interface = lldbServices.ILLDBServices;
            }
        }

        /// <summary>
        /// Loads and initializes the SOS module.
        /// </summary>
        /// <param name="tempDirectory">Temporary directory created to download DAC module</param>
        /// <param name="dacFilePath">The path to DAC that CLRMD loaded or downloaded or null</param>
        /// <param name="dbiFilePath">The path to DBI (for future use) or null</param>
        public void InitializeSOSHost(string tempDirectory, string dacFilePath, string dbiFilePath)
        {
            if (_sosLibrary == IntPtr.Zero)
            {
                string sosPath = Path.Combine(SOSPath, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "sos.dll" : "libsos.so");
                try
                {
                    _sosLibrary = DataTarget.PlatformFunctions.LoadLibrary(sosPath);
                }
                catch (DllNotFoundException ex)
                {
                    // This is a workaround for the Microsoft SDK docker images. Can fail when LoadLibrary uses libdl.so to load the SOS module.
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        throw new DllNotFoundException("Problem loading SOS module. Try installing libc6-dev (apt-get install libc6-dev) to work around this problem.", ex);
                    }
                    else
                    {
                        throw;
                    }
                }
                if (_sosLibrary == IntPtr.Zero)
                {
                    throw new FileNotFoundException($"SOS module {sosPath} not found");
                }
                IntPtr initializeAddress = DataTarget.PlatformFunctions.GetProcAddress(_sosLibrary, SOSInitialize);
                if (initializeAddress == IntPtr.Zero)
                {
                    throw new EntryPointNotFoundException($"Can not find SOS module initialization function: {SOSInitialize}");
                }
                var initializeFunc = (SOSInitializeDelegate)Marshal.GetDelegateForFunctionPointer(initializeAddress, typeof(SOSInitializeDelegate));

                // SOS depends on that the temp directory ends with "/".
                if (!string.IsNullOrEmpty(tempDirectory) && tempDirectory[tempDirectory.Length - 1] != Path.DirectorySeparatorChar)
                {
                    tempDirectory += Path.DirectorySeparatorChar;
                }

                int result = initializeFunc(
                    ref s_callbacks,
                    Marshal.SizeOf<SOSNetCoreCallbacks>(),
                    tempDirectory,
                    dacFilePath,
                    dbiFilePath,
                    SymbolReader.IsSymbolStoreEnabled());

                if (result != 0)
                {
                    throw new InvalidOperationException($"SOS initialization FAILED 0x{result:X8}");
                }
                Trace.TraceInformation("SOS initialized: tempDirectory '{0}' dacFilePath '{1}' sosPath '{2}'", tempDirectory, dacFilePath, sosPath);
            }
        }

        /// <summary>
        /// Execute a SOS command.
        /// </summary>
        /// <param name="commandLine">command name and arguments</param>
        public void ExecuteCommand(string commandLine)
        {
            string command = "Help";
            string arguments = null;

            if (commandLine != null)
            {
                int firstSpace = commandLine.IndexOf(' ');
                command = firstSpace == -1 ? commandLine : commandLine.Substring(0, firstSpace);
                arguments = firstSpace == -1 ? null : commandLine.Substring(firstSpace);
            }
            ExecuteCommand(command, arguments);
        }

        /// <summary>
        /// Execute a SOS command.
        /// </summary>
        /// <param name="command">just the command name</param>
        /// <param name="arguments">the command arguments and options</param>
        public void ExecuteCommand(string command, string arguments)
        {
            Debug.Assert(_sosLibrary != null);

            IntPtr commandAddress = DataTarget.PlatformFunctions.GetProcAddress(_sosLibrary, command);
            if (commandAddress == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException($"Can not find SOS command: {command}");
            }
            var commandFunc = (SOSCommandDelegate)Marshal.GetDelegateForFunctionPointer(commandAddress, typeof(SOSCommandDelegate));

            int result = commandFunc(_interface, arguments ?? "");
            if (result != 0)
            {
                throw new InvalidOperationException($"SOS command FAILED 0x{result:X8}");
            }
        }

        #region Reverse PInvoke Implementations

        internal static UIntPtr GetExpression(
            string expression)
        {
            if (expression != null)
            {
                if (ulong.TryParse(expression.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong result))
                {
                    return new UIntPtr(result);
                }
            }
            return UIntPtr.Zero;
        }

        internal int GetInterrupt(
            IntPtr self)
        {
            return _analyzeContext.CancellationToken.IsCancellationRequested ? S_OK : E_FAIL;
        }

        internal int OutputVaList(
            IntPtr self,
            DEBUG_OUTPUT mask,
            string format,
            IntPtr va_list)
        {
            try
            {
                // The text has already been formated by sos
                _console.Write(format);
            }
            catch (OperationCanceledException)
            {
                // ctrl-c interrupted the command
            }
            return S_OK;
        }

        internal unsafe int GetDebuggeeType(
            IntPtr self,
            DEBUG_CLASS* debugClass,
            DEBUG_CLASS_QUALIFIER* qualifier)
        {
            *debugClass = DEBUG_CLASS.USER_WINDOWS;
            *qualifier = DEBUG_CLASS_QUALIFIER.USER_WINDOWS_DUMP;
            return S_OK;
        }

        internal unsafe int GetDumpFormatFlags(
            IntPtr self,
            DEBUG_FORMAT* formatFlags)
        {
            *formatFlags = DEBUG_FORMAT.DEFAULT;
            return DebugClient.S_OK;
        }

        internal unsafe int GetPageSize(
            IntPtr self,
            uint* size)
        {
            *size = 4096;
            return S_OK;
        }

        internal unsafe int GetExecutingProcessorType(
            IntPtr self,
            IMAGE_FILE_MACHINE* type)
        {
            switch (DataReader.GetArchitecture())
            {
                case Microsoft.Diagnostics.Runtime.Architecture.Amd64:
                    *type = IMAGE_FILE_MACHINE.AMD64;
                    break;
                case Microsoft.Diagnostics.Runtime.Architecture.X86:
                    *type = IMAGE_FILE_MACHINE.I386;
                    break;
                case Microsoft.Diagnostics.Runtime.Architecture.Arm:
                    *type = IMAGE_FILE_MACHINE.THUMB2;
                    break;
                case Microsoft.Diagnostics.Runtime.Architecture.Arm64:
                    *type = IMAGE_FILE_MACHINE.ARM64;
                    break;
                default:
                    *type = IMAGE_FILE_MACHINE.UNKNOWN;
                    break;
            }
            return S_OK;
        }

        internal int Execute(
            IntPtr self,
            DEBUG_OUTCTL outputControl,
            string command,
            DEBUG_EXECUTE flags)
        {
            return E_NOTIMPL;
        }

        internal unsafe int GetLastEventInformation(
            IntPtr self,
            DEBUG_EVENT* type,
            uint* processId,
            uint* threadId,
            IntPtr extraInformation,
            uint extraInformationSize,
            uint* extraInformationUsed,
            StringBuilder description,
            uint descriptionSize,
            uint* descriptionUsed)
        {
            // Should never be called. This exception will take down the program.
            throw new NotImplementedException("GetLastEventInformation");
        }

        internal unsafe int Disassemble(
            IntPtr self,
            ulong offset,
            DEBUG_DISASM flags,
            StringBuilder buffer,
            uint bufferSize,
            uint* disassemblySize,
            ulong* endOffset)
        {
            buffer.Clear();
            Write(disassemblySize);
            Write(endOffset, offset);
            return E_NOTIMPL;
        }

        internal unsafe int ReadVirtual(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            uint bytesRequested,
            uint* pbytesRead)
        {
            if (DataReader.ReadMemory(address, buffer, unchecked((int)bytesRequested), out int bytesRead))
            {
                Write(pbytesRead, (uint)bytesRead);
                return S_OK;
            }
            return E_FAIL;
        }

        internal unsafe int ReadVirtualForWindows(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            uint bytesRequested,
            uint* pbytesRead)
        {
            if (DataReader.ReadMemory(address, buffer, unchecked((int)bytesRequested), out int bytesRead))
            {
                Write(pbytesRead, (uint)bytesRead);
                return S_OK;
            }

            // The memory read failed. Check if there is a module that contains the 
            // address range being read and map it into the virtual address space.
            foreach (ModuleInfo module in DataReader.EnumerateModules())
            {
                ulong start = module.ImageBase;
                ulong end = start + module.FileSize;
                if (start <= address && end > address)
                {
                    Trace.TraceInformation("ReadVirtualForWindows: address {0:X16} size {1:X8} found module {2}", address, bytesRequested, module.FileName);

                    // We found a module that contains the memory requested. Now find or download the PE image.
                    PEReader reader = GetPEReader(module);
                    if (reader != null)
                    {
                        // Read the memory from the PE image. There are a few limitions:
                        // 1) Fix ups are NOT applied to the sections
                        // 2) Memory regions that cross/contain heap memory into module image memory
                        int rva = (int)(address - start);
                        try
                        {
                            PEMemoryBlock block = reader.GetSectionData(rva);
                            if (block.Pointer == null)
                            {
                                Trace.TraceInformation("ReadVirtualForWindows: rva {0:X8} not in any section; reading from entire image", rva);

                                // If the address isn't contained in one of the sections, assume that SOS is reader the PE headers directly.
                                block = reader.GetEntireImage();
                            }
                            BlobReader blob = block.GetReader();
                            byte[] data = blob.ReadBytes((int)bytesRequested);

                            Marshal.Copy(data, 0, buffer, data.Length);
                            Write(pbytesRead, (uint)data.Length);
                            return S_OK;
                        }
                        catch (Exception ex) when (ex is BadImageFormatException || ex is InvalidOperationException || ex is IOException)
                        {
                            Trace.TraceError("ReadVirtualForWindows: exception {0}", ex);
                        }
                    }
                    break;
                }
            }

            return E_FAIL;
        }

        private PEReader GetPEReader(ModuleInfo module)
        {
            if (!_pathToPeReader.TryGetValue(module.FileName, out PEReader reader))
            {
                Stream stream = null;

                string downloadFilePath = module.FileName;
                if (!File.Exists(downloadFilePath)) 
                {
                    if (SymbolReader.IsSymbolStoreEnabled())
                    {
                        SymbolStoreKey key = PEFileKeyGenerator.GetKey(Path.GetFileName(downloadFilePath), module.TimeStamp, module.FileSize);
                        if (key != null)
                        {
                            // Now download the module from the symbol server
                            downloadFilePath = SymbolReader.GetSymbolFile(key);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(downloadFilePath))
                {
                    try
                    {
                        stream = File.OpenRead(downloadFilePath);
                    }
                    catch (Exception ex) when (ex is DirectoryNotFoundException || ex is FileNotFoundException || ex is UnauthorizedAccessException || ex is IOException)
                    {
                        Trace.TraceError("GetPEReader: exception {0}", ex);
                    }
                    if (stream != null)
                    {
                        reader = new PEReader(stream);
                        _pathToPeReader.Add(module.FileName, reader);
                    }
                }
            }
            return reader;
        }

        internal unsafe int WriteVirtual(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            uint bytesRequested,
            uint* pbytesWritten)
        {
            // This gets used by MemoryBarrier() calls in the dac, which really shouldn't matter what we do here.
            Write(pbytesWritten, bytesRequested);
            return S_OK;
        }

        internal int GetSymbolOptions(
            IntPtr self,
            out SYMOPT options)
        {
            options = SYMOPT.LOAD_LINES;
            return S_OK;
        }

        internal unsafe int GetNameByOffset(
            IntPtr self,
            ulong offset,
            StringBuilder nameBuffer,
            uint nameBufferSize,
            uint* nameSize,
            ulong* displacement)
        {
            nameBuffer.Clear();
            Write(nameSize);
            Write(displacement);
            return E_NOTIMPL;
        }

        internal int GetNumberModules(
            IntPtr self,
            out uint loaded,
            out uint unloaded)
        {
            loaded = (uint)DataReader.EnumerateModules().Count();
            unloaded = 0;
            return S_OK;
        }

        internal int GetModuleByIndex(
            IntPtr self,
            uint index,
            out ulong baseAddress)
        {
            baseAddress = 0;
            try
            {
                ModuleInfo module = DataReader.EnumerateModules().ElementAt((int)index);
                if (module == null)
                {
                    return E_FAIL;
                }
                baseAddress = module.ImageBase;
            }
            catch (ArgumentOutOfRangeException)
            {
                return E_FAIL;
            }
            return S_OK;
        }

        internal unsafe int GetModuleByModuleName(
            IntPtr self,
            string name,
            uint startIndex,
            uint* index,
            ulong* baseAddress)
        {
            Debug.Assert(startIndex == 0);
            Write(index);
            Write(baseAddress);

            uint i = 0;
            foreach (ModuleInfo module in DataReader.EnumerateModules())
            {
                if (IsModuleEqual(module, name))
                {
                    Write(index, i);
                    Write(baseAddress, module.ImageBase);
                    return S_OK;
                }
                i++;
            }
            return E_FAIL;
        }

        internal unsafe int GetModuleByOffset(
            IntPtr self,
            ulong offset,
            uint startIndex,
            uint* index,
            ulong* baseAddress)
        {
            Write(index);
            Write(baseAddress);
            return E_NOTIMPL;
        }

        internal unsafe int GetModuleNames(
            IntPtr self,
            uint index,
            ulong baseAddress,
            StringBuilder imageNameBuffer,
            uint imageNameBufferSize,
            uint* imageNameSize,
            StringBuilder moduleNameBuffer,
            uint moduleNameBufferSize,
            uint* moduleNameSize,
            StringBuilder loadedImageNameBuffer,
            uint loadedImageNameBufferSize,
            uint* loadedImageNameSize)
        {
            Write(imageNameSize);
            Write(moduleNameSize);
            Write(loadedImageNameSize);

            uint i = 0;
            foreach (ModuleInfo module in DataReader.EnumerateModules())
            {
                if (index != uint.MaxValue && i == index || index == uint.MaxValue && baseAddress == module.ImageBase)
                {
                    if (imageNameBuffer != null) {
                        imageNameBuffer.Append(module.FileName);
                    }
                    Write(imageNameSize, (uint)module.FileName.Length + 1);

                    string moduleName = GetModuleName(module);
                    if (moduleNameBuffer != null) {
                        moduleNameBuffer.Append(moduleName);
                    }
                    Write(moduleNameSize, (uint)moduleName.Length + 1);
                    return S_OK;
                }
                i++;
            }
            return E_FAIL;
        }

        internal unsafe int GetModuleParameters(
            IntPtr self,
            uint count,
            ulong* bases,
            uint start,
            DEBUG_MODULE_PARAMETERS* moduleParams)
        {
            Debug.Assert(bases != null);
            Debug.Assert(start == 0);

            foreach (ModuleInfo module in DataReader.EnumerateModules())
            {
                for (int i = 0; i < count; i++)
                {
                    if (bases[i] == module.ImageBase)
                    {
                        moduleParams[i].Base = module.ImageBase;
                        moduleParams[i].Size = module.FileSize;
                        moduleParams[i].TimeDateStamp = module.TimeStamp;
                        moduleParams[i].Checksum = 0;
                        moduleParams[i].Flags = DEBUG_MODULE.LOADED;
                        moduleParams[i].SymbolType = DEBUG_SYMTYPE.PDB;

                        uint imageNameSize = (uint)module.FileName.Length + 1;
                        moduleParams[i].ImageNameSize = imageNameSize;

                        string moduleName = GetModuleName(module);
                        uint moduleNameSize = (uint)moduleName.Length + 1;
                        moduleParams[i].ModuleNameSize = moduleNameSize;

                        moduleParams[i].LoadedImageNameSize = 0;
                        moduleParams[i].SymbolFileNameSize = 0;
                        moduleParams[i].MappedImageNameSize = 0;
                    }
                }
            }
            return S_OK;
        }

        internal unsafe int GetLineByOffset(
            IntPtr self,
            ulong offset,
            uint* line,
            StringBuilder fileBuffer,
            uint fileBufferSize,
            uint* fileSize,
            ulong* displacement)
        {
            Write(line);
            Write(fileSize);
            Write(displacement);
            return E_NOTIMPL;
        }

        internal unsafe int GetSourceFileLineOffsets(
            IntPtr self,
            string file,
            ulong[] buffer,
            uint bufferLines,
            uint* fileLines)
        {
            Write(fileLines);
            return E_NOTIMPL;
        }

        internal unsafe int FindSourceFile(
            IntPtr self,
            uint startElement,
            string file,
            DEBUG_FIND_SOURCE flags,
            uint* foundElement,
            StringBuilder buffer,
            uint bufferSize,
            uint* foundSize)
        {
            Write(foundElement);
            Write(foundSize);
            return E_NOTIMPL;
        }

        internal unsafe int GetSymbolPath(
            IntPtr self,
            StringBuilder buffer,
            int bufferSize,
            uint* pathSize)
        {
            if (buffer != null) {
                buffer.Clear();
            }
            Write(pathSize);
            return S_OK;
        }

        internal int GetThreadContext(
            IntPtr self,
            IntPtr context,
            uint contextSize)
        {
            uint threadId = (uint)_analyzeContext.CurrentThreadId;
            if (!DataReader.GetThreadContext(threadId, uint.MaxValue, contextSize, context)) {
                return E_FAIL;
            }
            return S_OK;
        }

        internal int SetThreadContext(
            IntPtr self,
            IntPtr context,
            uint contextSize)
        {
            return DebugClient.NotImplemented;
        }

        internal int GetNumberThreads(
            IntPtr self,
            out uint number)
        {
            number = (uint)DataReader.EnumerateAllThreads().Count();
            return DebugClient.S_OK;
        }

        internal int GetTotalNumberThreads(
            IntPtr self,
            out uint total,
            out uint largestProcess)
        {
            total = (uint)DataReader.EnumerateAllThreads().Count();
            largestProcess = total;
            return DebugClient.S_OK;
        }

        internal int GetCurrentProcessId(
            IntPtr self,
            out uint id)
        {
            id = 0;
            if (DataReader is IDataReader2 dataReader2) {
                id = dataReader2.ProcessId;
            }
            return S_OK;
        }

        internal int GetCurrentThreadId(
            IntPtr self,
            out uint id)
        {
            return GetThreadIdBySystemId(self, (uint)_analyzeContext.CurrentThreadId, out id);
        }

        internal int SetCurrentThreadId(
            IntPtr self,
            uint id)
        {
            try
            {
                unchecked {
                    _analyzeContext.CurrentThreadId = (int)DataReader.EnumerateAllThreads().ElementAt((int)id);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return E_FAIL;
            }
            return S_OK;
        }

        internal int GetCurrentThreadSystemId(
            IntPtr self,
            out uint sysId)
        {
            sysId = (uint)_analyzeContext.CurrentThreadId;
            return S_OK;
        }

        internal unsafe int GetThreadIdsByIndex(
            IntPtr self,
            uint start,
            uint count,
            uint* ids,
            uint* sysIds)
        {
            uint id = 0;
            int index = 0;
            foreach (uint s in DataReader.EnumerateAllThreads())
            {
                if (id >= count) {
                    break;
                }
                if (id >= start)
                {
                    if (ids != null) {
                        ids[index] = id;
                    }
                    if (sysIds != null) {
                        sysIds[index] = s;
                    }
                    index++;
                }
                id++;
            }
            return DebugClient.S_OK;
        }

        internal int GetThreadIdBySystemId(
            IntPtr self,
            uint sysId,
            out uint id)
        {
            id = 0;
            if (sysId != 0)
            {
                foreach (uint s in DataReader.EnumerateAllThreads())
                {
                    if (s == sysId) {
                        return S_OK;
                    }
                    id++;
                }
            }
            return E_FAIL;
        }

        internal unsafe int GetCurrentThreadTeb(
            IntPtr self,
            ulong* offset)
        {
            uint threadId = (uint)_analyzeContext.CurrentThreadId;
            ulong teb = DataReader.GetThreadTeb(threadId);
            Write(offset, teb);
            return S_OK;
        }

        internal int GetInstructionOffset(
            IntPtr self,
            out ulong offset)
        {
            return GetRegister(_registerService.InstructionPointerIndex, out offset);
        }

        internal int GetStackOffset(
            IntPtr self,
            out ulong offset)
        {
            return GetRegister(_registerService.StackPointerIndex, out offset);
        }

        internal int GetFrameOffset(
            IntPtr self,
            out ulong offset)
        {
            return GetRegister(_registerService.FramePointerIndex, out offset);
        }

        internal int GetIndexByName(
            IntPtr self,
            string name,
            out uint index)
        {
            if (_registerService.GetRegisterIndexByName(name, out int value)) {
                index = 0;
                return E_INVALIDARG;
            }
            index = (uint)value;
            return S_OK;
        }

        internal int GetValue(
            IntPtr self,
            uint register,
            out DEBUG_VALUE value)
        {
            int hr = GetRegister((int)register, out ulong offset);

            // SOS expects the DEBUG_VALUE field to be set based on the 
            // processor architecture instead of the register size.
            switch (DataReader.GetPointerSize())
            {
                case 8:
                    value = new DEBUG_VALUE {
                        Type = DEBUG_VALUE_TYPE.INT64,
                        I64 = offset
                    };
                    break;

                case 4:
                    value = new DEBUG_VALUE {
                        Type = DEBUG_VALUE_TYPE.INT32,
                        I32 = (uint)offset
                    };
                    break;

                default:
                    value = new DEBUG_VALUE();
                    hr = E_FAIL;
                    break;
            }
            return hr;
        }

        internal int GetRegister(
            string register,
            out ulong value)
        {
            value = 0;
            if (!_registerService.GetRegisterIndexByName(register, out int index)) {
                return E_INVALIDARG;
            }
            return GetRegister(index, out value);
        }

        internal int GetRegister(
            int index, 
            out ulong value)
        {
            uint threadId = (uint)_analyzeContext.CurrentThreadId;
            if (!_registerService.GetRegisterValue(threadId, index, out value)) {
                return E_FAIL;
            }
            return S_OK;
        }

        internal static bool IsRuntimeModule(ModuleInfo module)
        {
            return IsModuleEqual(module, s_coreclrModuleName);
        }

        internal static bool IsModuleEqual(ModuleInfo module, string moduleName)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                return StringComparer.OrdinalIgnoreCase.Equals(GetModuleName(module), moduleName);
            }
            else {
                return string.Equals(GetModuleName(module), moduleName);
            }
        }

        internal static string GetModuleName(ModuleInfo module)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                return Path.GetFileNameWithoutExtension(module.FileName);
            }
            else {
                return Path.GetFileName(module.FileName);
            }
        }

        internal unsafe static void Write(uint* pointer, uint value = 0)
        {
            if (pointer != null) {
                *pointer = value;
            }
        }

        internal unsafe static void Write(ulong* pointer, ulong value = 0)
        {
            if (pointer != null) {
                *pointer = value;
            }
        }

        #endregion
    }
}
