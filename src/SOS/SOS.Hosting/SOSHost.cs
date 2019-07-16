// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private delegate ulong GetExpressionDelegate(
            [In, MarshalAs(UnmanagedType.LPStr)] string expression);

        private const string SOSInitialize = "SOSInitializeByHost";

        #region SOS.NETCore function delegates

        private delegate bool InitializeSymbolStoreDelegate(
            bool logging,
            bool msdl,
            bool symweb,
            string symbolServerPath,
            string symbolCachePath,
            string windowsSymbolPath);

        private delegate void DisplaySymbolStoreDelegate();

        private delegate void DisableSymbolStoreDelegate();

        private delegate void LoadNativeSymbolsDelegate(
            SymbolReader.SymbolFileCallback callback,
            IntPtr parameter,
            string tempDirectory,
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
        internal readonly ISOSHostContext SOSHostContext;

        private static readonly string s_coreclrModuleName;
        private static readonly Dictionary<string, int> s_registersByName;
        private static readonly int[] s_registerOffsets;

        private readonly COMCallableIUnknown _ccw;  
        private readonly IntPtr _interface;
        private IntPtr _sosLibrary = IntPtr.Zero;

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

            // TODO: Support other architectures
            Type contextType = typeof(AMD64Context);
            var registerNames = new Dictionary<string, int>();
            var offsets = new List<int>();
            int index = 0;

            FieldInfo[] fields = contextType.GetFields(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo field in fields) {
                registerNames.Add(field.Name.ToLower(), index);

                FieldOffsetAttribute attribute = field.GetCustomAttributes<FieldOffsetAttribute>(inherit: false).Single();
                offsets.Insert(index, attribute.Value);
                index++;
            }
            s_registersByName = registerNames;
            s_registerOffsets = offsets.ToArray();
        }

        /// <summary>
        /// The native SOS binaries path. Default is OS/architecture (RID) named directory in the same directory as this assembly.
        /// </summary>
        public string SOSPath { get; set; }

        /// <summary>
        /// Create an instance of the hosting class
        /// </summary>
        public SOSHost(IDataReader dataReader, ISOSHostContext context)
        {
            DataReader = dataReader;
            SOSHostContext = context;
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
                    tempDirectory = tempDirectory + Path.DirectorySeparatorChar;
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

        internal static ulong GetExpression(
            string expression)
        {
            if (expression != null)
            {
                if (ulong.TryParse(expression.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong result))
                {
                    return result;
                }
            }
            return 0;
        }

        internal int GetInterrupt(
            IntPtr self)
        {
            return SOSHostContext.CancellationToken.IsCancellationRequested ? S_OK : E_FAIL;
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
                SOSHostContext.Write(format);
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
                    *type = IMAGE_FILE_MACHINE.ARM;
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
            uint threadId = (uint)SOSHostContext.CurrentThreadId;
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
            return GetThreadIdBySystemId(self, (uint)SOSHostContext.CurrentThreadId, out id);
        }

        internal int SetCurrentThreadId(
            IntPtr self,
            uint id)
        {
            try
            {
                unchecked {
                    SOSHostContext.CurrentThreadId = (int)DataReader.EnumerateAllThreads().ElementAt((int)id);
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
            sysId = (uint)SOSHostContext.CurrentThreadId;
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
            uint threadId = (uint)SOSHostContext.CurrentThreadId;
            ulong teb = DataReader.GetThreadTeb(threadId);
            Write(offset, teb);
            return S_OK;
        }

        internal int GetInstructionOffset(
            IntPtr self,
            out ulong offset)
        {
            // TODO: Support other architectures
            return GetRegister("rip", out offset);
        }

        internal int GetStackOffset(
            IntPtr self,
            out ulong offset)
        {
            // TODO: Support other architectures
            return GetRegister("rsp", out offset);
        }

        internal int GetFrameOffset(
            IntPtr self,
            out ulong offset)
        {
            // TODO: Support other architectures
            return GetRegister("rbp", out offset);
        }

        internal int GetIndexByName(
            IntPtr self,
            string name,
            out uint index)
        {
            if (!s_registersByName.TryGetValue(name, out int value)) {
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
            return GetRegister((int)register, out value);
        }

        internal unsafe int GetRegister(
            string register,
            out ulong value)
        {
            value = 0;
            if (!s_registersByName.TryGetValue(register, out int index)) {
                return E_INVALIDARG;
            }
            int hr = GetRegister(index, out DEBUG_VALUE debugValue);
            if (hr != S_OK) {
                return hr;
            }
            // TODO: Support other architectures
            value = debugValue.I64;
            return S_OK;
        }

        internal unsafe int GetRegister(
            int index, 
            out DEBUG_VALUE value)
        {
            value = new DEBUG_VALUE();

            if (index >= s_registerOffsets.Length) {
                return E_INVALIDARG;
            }
            uint threadId = (uint)SOSHostContext.CurrentThreadId;

            // TODO: Support other architectures
            byte[] buffer = new byte[AMD64Context.Size];
            fixed (byte* ptr = buffer)
            {
                if (!DataReader.GetThreadContext(threadId, uint.MaxValue, (uint)AMD64Context.Size, new IntPtr(ptr))) {
                    return E_FAIL;
                }
                int offset = s_registerOffsets[index];
                value.I64 = *((ulong*)(ptr + offset));
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
