﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Architecture = System.Runtime.InteropServices.Architecture;

namespace SOS.Hosting
{
    /// <summary>
    /// Helper code to hosting SOS under ClrMD
    /// </summary>
    public sealed class SOSHost
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SOSCommandDelegate(
            IntPtr ILLDBServices,
            [In, MarshalAs(UnmanagedType.LPStr)] string args);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SOSInitializeDelegate(
            IntPtr IHost);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void SOSUninitializeDelegate();

        private const string SOSInitialize = "SOSInitializeByHost";
        private const string SOSUninitialize = "SOSUninitializeByHost";

        // This is what dbgeng/IDebuggerServices returns for non-PE modules that don't have a timestamp
        internal const uint InvalidTimeStamp = 0xFFFFFFFE;
        internal const uint InvalidChecksum = 0xFFFFFFFF;

        internal readonly ITarget Target;
        internal readonly IConsoleService ConsoleService;
        internal readonly IModuleService ModuleService;
        internal readonly IThreadService ThreadService;
        internal readonly IMemoryService MemoryService;
        private readonly IntPtr _interface;
        private readonly HostWrapper _hostWrapper;
        private readonly ulong _ignoreAddressBitsMask;
        private IntPtr _sosLibrary = IntPtr.Zero;

        /// <summary>
        /// The native SOS binaries path. Default is OS/architecture (RID) named directory in the same directory as this assembly.
        /// </summary>
        public string SOSPath { get; set; }

        /// <summary>
        /// Create an instance of the hosting class
        /// </summary>
        /// <param name="target">target instance</param>
        public SOSHost(ITarget target)
        {
            Target = target;
            ConsoleService = target.Services.GetService<IConsoleService>();
            ModuleService = target.Services.GetService<IModuleService>();
            ThreadService = target.Services.GetService<IThreadService>();
            MemoryService = target.Services.GetService<IMemoryService>();
            _ignoreAddressBitsMask = MemoryService.SignExtensionMask();

            string rid = InstallHelper.GetRid();
            SOSPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), rid);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var debugClient = new DebugClient(this);
                _interface = debugClient.IDebugClient;
            }
            else
            {
                var lldbServices = new LLDBServices(this);
                _interface = lldbServices.ILLDBServices;
            }
            _hostWrapper = new HostWrapper(target.Host);
            _hostWrapper.AddServiceWrapper(SymbolServiceWrapper.IID_ISymbolService, () => new SymbolServiceWrapper(target.Host));
        }

        /// <summary>
        /// Loads and initializes the SOS module.
        /// </summary>
        public void InitializeSOSHost()
        {
            if (_sosLibrary == IntPtr.Zero)
            {
                string sos;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    sos = "sos.dll";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                    sos = "libsos.so";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    sos = "libsos.dylib";
                }
                else {
                    throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");
                }
                string sosPath = Path.Combine(SOSPath, sos);
                try
                {
                    _sosLibrary = Microsoft.Diagnostics.Runtime.DataTarget.PlatformFunctions.LoadLibrary(sosPath);
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
                var initializeFunc = GetDelegateFunction<SOSInitializeDelegate>(_sosLibrary, SOSInitialize);
                if (initializeFunc == null)
                {
                    throw new EntryPointNotFoundException($"Can not find SOS module initialization function: {SOSInitialize}");
                }
                Target.DisposeOnClose(Target.Host.OnShutdownEvent.Register(OnShutdownEvent));
                int result = initializeFunc(_hostWrapper.IHost);
                if (result != 0)
                {
                    throw new InvalidOperationException($"SOS initialization FAILED 0x{result:X8}");
                }
                Trace.TraceInformation("SOS initialized: sosPath '{0}'", sosPath);
            }
        }

        /// <summary>
        /// Shutdown/clean up the native SOS.
        /// </summary>
        private void OnShutdownEvent()
        {
            Trace.TraceInformation("SOSHost: OnShutdownEvent");
            var uninitializeFunc = GetDelegateFunction<SOSUninitializeDelegate>(_sosLibrary, SOSUninitialize);
            uninitializeFunc?.Invoke();
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

            var commandFunc = GetDelegateFunction<SOSCommandDelegate>(_sosLibrary, command);
            if (commandFunc == null)
            {
                throw new EntryPointNotFoundException($"Can not find SOS command: {command}");
            }
            int result = commandFunc(_interface, arguments ?? "");
            if (result != HResult.S_OK)
            {
                Trace.TraceError($"SOS command FAILED 0x{result:X8}");
            }
        }

        #region Reverse PInvoke Implementations

        internal int GetInterrupt(
            IntPtr self)
        {
            return ConsoleService.CancellationToken.IsCancellationRequested ? HResult.S_OK : HResult.E_FAIL;
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
                ConsoleService.Write(format);
            }
            catch (OperationCanceledException)
            {
                // ctrl-c interrupted the command
            }
            return HResult.S_OK;
        }

        internal unsafe int GetDebuggeeType(
            IntPtr self,
            DEBUG_CLASS* debugClass,
            DEBUG_CLASS_QUALIFIER* qualifier)
        {
            *debugClass = DEBUG_CLASS.USER_WINDOWS;
            *qualifier = DEBUG_CLASS_QUALIFIER.USER_WINDOWS_DUMP;
            return HResult.S_OK;
        }

        internal unsafe int GetDumpFormatFlags(
            IntPtr self,
            DEBUG_FORMAT* formatFlags)
        {
            *formatFlags = DEBUG_FORMAT.DEFAULT;
            return HResult.S_OK;
        }

        internal unsafe int GetPageSize(
            IntPtr self,
            uint* size)
        {
            *size = 4096;
            return HResult.S_OK;
        }

        internal unsafe int GetExecutingProcessorType(
            IntPtr self,
            IMAGE_FILE_MACHINE* type)
        {
            switch (Target.Architecture)
            {
                case Architecture.X64:
                    *type = IMAGE_FILE_MACHINE.AMD64;
                    break;
                case Architecture.X86:
                    *type = IMAGE_FILE_MACHINE.I386;
                    break;
                case Architecture.Arm:
                    *type = IMAGE_FILE_MACHINE.THUMB2;
                    break;
                case Architecture.Arm64:
                    *type = IMAGE_FILE_MACHINE.ARM64;
                    break;
                default:
                    *type = IMAGE_FILE_MACHINE.UNKNOWN;
                    break;
            }
            return HResult.S_OK;
        }

        internal int Execute(
            IntPtr self,
            DEBUG_OUTCTL outputControl,
            string command,
            DEBUG_EXECUTE flags)
        {
            return HResult.E_NOTIMPL;
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
            return HResult.E_NOTIMPL;
        }

        internal unsafe int ReadVirtual(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            uint bytesRequested,
            uint* pbytesRead)
        {
            address &= _ignoreAddressBitsMask;
            if (MemoryService.ReadMemory(address, buffer, unchecked((int)bytesRequested), out int bytesRead))
            {
                Write(pbytesRead, (uint)bytesRead);
                return HResult.S_OK;
            }
            return HResult.E_FAIL;
        }

        internal unsafe int WriteVirtual(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            uint bytesRequested,
            uint* pbytesWritten)
        {
            address &= _ignoreAddressBitsMask;
            if (MemoryService.WriteMemory(address, new Span<byte>(buffer.ToPointer(), unchecked((int)bytesRequested)), out int bytesWritten))
            {
                Write(pbytesWritten, (uint)bytesWritten);
                return HResult.S_OK;
            }
            return HResult.E_FAIL;
        }

        internal int GetSymbolOptions(
            IntPtr self,
            out SYMOPT options)
        {
            options = SYMOPT.LOAD_LINES;
            return HResult.S_OK;
        }

        internal unsafe int GetNameByOffset(
            IntPtr self,
            ulong offset,
            StringBuilder nameBuffer,
            uint nameBufferSize,
            uint* nameSize,
            ulong* displacement)
        {
            nameBuffer?.Clear();
            Write(nameSize);
            Write(displacement);
            return HResult.E_NOTIMPL;
        }

        internal int GetNumberModules(
            IntPtr self,
            out uint loaded,
            out uint unloaded)
        {
            loaded = (uint)ModuleService.EnumerateModules().Count();
            unloaded = 0;
            return HResult.S_OK;
        }

        internal int GetModuleByIndex(
            IntPtr self,
            uint index,
            out ulong baseAddress)
        {
            baseAddress = 0;
            try
            {
                baseAddress = ModuleService.GetModuleFromIndex((int)index).ImageBase;
            }
            catch (DiagnosticsException)
            {
                return HResult.E_FAIL;
            }
            return HResult.S_OK;
        }

        internal unsafe int GetModuleByModuleName(
            IntPtr self,
            string name,
            uint startIndex,
            uint* index,
            ulong* baseAddress)
        {
            Write(index);
            Write(baseAddress);

            if (startIndex != 0)
            {
                return HResult.E_INVALIDARG;
            }
            if (Target.OperatingSystem == OSPlatform.Windows)
            {
                name = Path.GetFileNameWithoutExtension(name) + ".dll";
            }
            IModule module = ModuleService.GetModuleFromModuleName(name).FirstOrDefault();
            if (module != null)
            {
                Write(index, (uint)module.ModuleIndex);
                Write(baseAddress, module.ImageBase);
                return HResult.S_OK;
            }
            return HResult.E_FAIL;
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

            if (startIndex != 0)
            {
                return HResult.E_INVALIDARG;
            }
            IModule module = ModuleService.GetModuleFromAddress(offset);
            if (module != null)
            {
                Write(index, (uint)module.ModuleIndex);
                Write(baseAddress, module.ImageBase);
                return HResult.S_OK;
            }
            return HResult.E_FAIL;
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
 
            IModule module;
            try 
            { 
                if (index != uint.MaxValue) 
                {
                    module = ModuleService.GetModuleFromIndex(unchecked((int)index));
                }
                else
                {
                    module = ModuleService.GetModuleFromBaseAddress(baseAddress);
                }
            }
            catch (DiagnosticsException)
            {
                return HResult.E_FAIL;
            }
            imageNameBuffer?.Append(module.FileName);
            Write(imageNameSize, (uint)module.FileName.Length + 1);

            string moduleName = GetFileName(module.FileName);
            moduleNameBuffer?.Append(moduleName);
            Write(moduleNameSize, (uint)moduleName.Length + 1);
            return HResult.S_OK;
        }

        internal unsafe int GetModuleParameters(
            IntPtr self,
            uint count,
            ulong* bases,
            uint start,
            DEBUG_MODULE_PARAMETERS* moduleParams)
        {
            if (start != 0 || bases == null)
            {
                return HResult.E_INVALIDARG;
            }
            foreach (IModule module in ModuleService.EnumerateModules())
            {
                for (int i = 0; i < count; i++)
                {
                    if (bases[i] == module.ImageBase)
                    {
                        moduleParams[i].Base = module.ImageBase;
                        moduleParams[i].Size = (uint)module.ImageSize;
                        moduleParams[i].TimeDateStamp = module.IndexTimeStamp.GetValueOrDefault(InvalidTimeStamp);
                        moduleParams[i].Checksum = InvalidChecksum;
                        moduleParams[i].Flags = DEBUG_MODULE.LOADED;
                        moduleParams[i].SymbolType = DEBUG_SYMTYPE.PDB;

                        uint imageNameSize = (uint)module.FileName.Length + 1;
                        moduleParams[i].ImageNameSize = imageNameSize;

                        string moduleName = GetFileName(module.FileName);
                        uint moduleNameSize = (uint)moduleName.Length + 1;
                        moduleParams[i].ModuleNameSize = moduleNameSize;

                        moduleParams[i].LoadedImageNameSize = 0;
                        moduleParams[i].SymbolFileNameSize = 0;
                        moduleParams[i].MappedImageNameSize = 0;
                    }
                }
            }
            return HResult.S_OK;
        }

        internal unsafe int GetModuleVersionInformation(
            IntPtr self,
            uint index,
            ulong baseAddress,
            [MarshalAs(UnmanagedType.LPStr)] string item,
            byte* buffer,
            uint bufferSize,
            uint* verInfoSize)
        {
            if (item == null || buffer == null || bufferSize == 0)
            {
                return HResult.E_INVALIDARG;
            }
            IModule module;
            try 
            { 
                if (index != uint.MaxValue) 
                {
                    module = ModuleService.GetModuleFromIndex(unchecked((int)index));
                }
                else
                {
                    module = ModuleService.GetModuleFromBaseAddress(baseAddress);
                }
            }
            catch (DiagnosticsException)
            {
                return HResult.E_FAIL;
            }
            if (item == "\\")
            {
                int versionSize = Marshal.SizeOf(typeof(VS_FIXEDFILEINFO));
                Write(verInfoSize, (uint)versionSize);
                if (bufferSize < versionSize)
                {
                    return HResult.E_INVALIDARG;
                }
                if (!module.Version.HasValue)
                {
                    return HResult.E_FAIL;
                }
                VS_FIXEDFILEINFO* fileInfo = (VS_FIXEDFILEINFO*)buffer;
                fileInfo->dwSignature = 0;
                fileInfo->dwStrucVersion = 0;
                fileInfo->dwFileFlagsMask = 0;
                fileInfo->dwFileFlags = 0;

                VersionInfo versionInfo = module.Version.Value;
                fileInfo->dwFileVersionMS = (uint)versionInfo.Minor | (uint)versionInfo.Major << 16;
                fileInfo->dwFileVersionLS = (uint)versionInfo.Patch | (uint)versionInfo.Revision << 16;
            }
            else if (item == "\\StringFileInfo\\040904B0\\FileVersion")
            {
                *buffer = 0;
                string versionString = module.VersionString;
                if (versionString == null)
                {
                    return HResult.E_FAIL;
                }
                try
                {
                    byte[] source = Encoding.ASCII.GetBytes(versionString + '\0');
                    Marshal.Copy(source, 0, new IntPtr(buffer), Math.Min(source.Length, (int)bufferSize));
                }
                catch (ArgumentOutOfRangeException)
                {
                    return HResult.E_INVALIDARG;
                }
            }
            else
            {
                return HResult.E_INVALIDARG;
            }
            return HResult.S_OK;
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
            return HResult.E_NOTIMPL;
        }

        internal unsafe int GetSourceFileLineOffsets(
            IntPtr self,
            string file,
            ulong[] buffer,
            uint bufferLines,
            uint* fileLines)
        {
            Write(fileLines);
            return HResult.E_NOTIMPL;
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
            return HResult.E_NOTIMPL;
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
            return HResult.S_OK;
        }

        internal int GetThreadContext(
            IntPtr self,
            IntPtr context,
            uint contextSize)
        {
            if (!ThreadService.CurrentThreadId.HasValue)
            {
                return HResult.E_FAIL;
            }
            return GetThreadContextBySystemId(self, ThreadService.CurrentThreadId.Value, 0, contextSize, context);
        }

        internal int GetThreadContextBySystemId(
            IntPtr self,
            uint threadId,
            uint contextFlags,
            uint contextSize,
            IntPtr context)
        {
            byte[] registerContext;
            try
            {
                registerContext = ThreadService.GetThreadFromId(threadId).GetThreadContext();
            }
            catch (DiagnosticsException)
            {
                return HResult.E_FAIL;
            }
            try
            {
                Marshal.Copy(registerContext, 0, context, (int)contextSize);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException || ex is ArgumentNullException)
            {
                return HResult.E_INVALIDARG;
            }
            return HResult.S_OK;
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
            number = (uint)ThreadService.EnumerateThreads().Count();
            return HResult.S_OK;
        }

        internal int GetTotalNumberThreads(
            IntPtr self,
            out uint total,
            out uint largestProcess)
        {
            total = (uint)ThreadService.EnumerateThreads().Count();
            largestProcess = total;
            return HResult.S_OK;
        }

        internal int GetCurrentProcessSystemId(
            IntPtr self,
            out uint id)
        {
            if (!Target.ProcessId.HasValue) {
                id = 0;
                return HResult.E_FAIL;
            }
            id = Target.ProcessId.Value;
            return HResult.S_OK;
        }

        internal int GetCurrentThreadId(
            IntPtr self,
            out uint id)
        {
            if (!ThreadService.CurrentThreadId.HasValue) {
                id = 0;
                return HResult.E_FAIL;
            }
            return GetThreadIdBySystemId(self, ThreadService.CurrentThreadId.Value, out id);
        }

        internal int SetCurrentThreadId(
            IntPtr self,
            uint id)
        {
            try
            {
                ThreadService.CurrentThreadId = ThreadService.GetThreadFromIndex(unchecked((int)id)).ThreadId;
            }
            catch (DiagnosticsException)
            {
                return HResult.E_FAIL;
            }
            return HResult.S_OK;
        }

        internal int GetCurrentThreadSystemId(
            IntPtr self,
            out uint sysId)
        {
            uint? id = ThreadService.CurrentThreadId;
            if (id.HasValue)
            {
                sysId = id.Value;
                return HResult.S_OK;
            }
            sysId = 0;
            return HResult.E_FAIL;
        }

        internal unsafe int GetThreadIdsByIndex(
            IntPtr self,
            uint start,
            uint count,
            uint* ids,
            uint* sysIds)
        {
            int number = ThreadService.EnumerateThreads().Count();
            if (start >= number || start + count > number)
            {
                return HResult.E_INVALIDARG;
            }
            int index = 0;
            foreach (IThread threadInfo in ThreadService.EnumerateThreads())
            {
                if (index >= start && index < start + count)
                {
                    if (ids != null) {
                        ids[index] = (uint)threadInfo.ThreadIndex;
                    }
                    if (sysIds != null) {
                        sysIds[index] = threadInfo.ThreadId;
                    }
                }
                index++;
            }
            return HResult.S_OK;
        }

        internal int GetThreadIdBySystemId(
            IntPtr self,
            uint sysId,
            out uint id)
        {
            if (sysId != 0)
            {
                try
                {
                    IThread threadInfo = ThreadService.GetThreadFromId(sysId);
                    id = (uint)threadInfo.ThreadIndex;
                    return HResult.S_OK;
                }
                catch (DiagnosticsException)
                {
                }
            }
            id = 0;
            return HResult.E_FAIL;
        }

        internal unsafe int GetCurrentThreadTeb(
            IntPtr self,
            ulong* offset)
        {
            if (ThreadService.CurrentThreadId.HasValue)
            {
                uint threadId = ThreadService.CurrentThreadId.Value;
                try
                {
                    ulong teb = ThreadService.GetThreadFromId(threadId).GetThreadTeb();
                    Write(offset, teb);
                    return HResult.S_OK;
                }
                catch (DiagnosticsException)
                {
                }
            }
            Write(offset, 0);
            return HResult.E_FAIL;
        }

        internal int GetInstructionOffset(
            IntPtr self,
            out ulong offset)
        {
            return GetRegister(ThreadService.InstructionPointerIndex, out offset);
        }

        internal int GetStackOffset(
            IntPtr self,
            out ulong offset)
        {
            return GetRegister(ThreadService.StackPointerIndex, out offset);
        }

        internal int GetFrameOffset(
            IntPtr self,
            out ulong offset)
        {
            return GetRegister(ThreadService.FramePointerIndex, out offset);
        }

        internal int GetIndexByName(
            IntPtr self,
            string name,
            out uint index)
        {
            if (!ThreadService.TryGetRegisterIndexByName(name, out int value)) {
                index = 0;
                return HResult.E_INVALIDARG;
            }
            index = (uint)value;
            return HResult.S_OK;
        }

        internal int GetValue(
            IntPtr self,
            uint register,
            out DEBUG_VALUE value)
        {
            int hr = GetRegister((int)register, out ulong offset);

            // SOS expects the DEBUG_VALUE field to be set based on the 
            // processor architecture instead of the register size.
            switch (MemoryService.PointerSize)
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
                    hr = HResult.E_FAIL;
                    break;
            }
            return hr;
        }

        internal int GetRegister(
            string register,
            out ulong value)
        {
            if (!ThreadService.TryGetRegisterIndexByName(register, out int index)) {
                value = 0;
                return HResult.E_INVALIDARG;
            }
            return GetRegister(index, out value);
        }

        internal int GetRegister(
            int index, 
            out ulong value)
        {
            if (ThreadService.CurrentThreadId.HasValue)
            {
                IThread thread = ThreadService.GetThreadFromId(ThreadService.CurrentThreadId.Value);
                if (thread != null)
                {
                    if (thread.TryGetRegisterValue(index, out value))
                    {
                        return HResult.S_OK;
                    }
                }
            }
            value = 0;
            return HResult.E_FAIL;
        }

        #endregion

        /// <summary>
        /// Helper function to get pinvoke entries into native modules
        /// </summary>
        /// <typeparam name="T">function delegate</typeparam>
        /// <param name="library">module name</param>
        /// <param name="functionName">name of function</param>
        /// <returns>delegate instance or null</returns>
        public static T GetDelegateFunction<T>(IntPtr library, string functionName)
            where T : Delegate
        {
            IntPtr functionAddress = Microsoft.Diagnostics.Runtime.DataTarget.PlatformFunctions.GetLibraryExport(library, functionName);
            if (functionAddress == IntPtr.Zero) {
                return default;
            }
            return (T)Marshal.GetDelegateForFunctionPointer(functionAddress, typeof(T));
        }

        private string GetFileName(string fileName) => Target.OperatingSystem == OSPlatform.Windows ? Path.GetFileNameWithoutExtension(fileName) : Path.GetFileName(fileName);

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
    }
}
