// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS
{
    internal unsafe class LLDBServicesWrapper : COMCallableIUnknown
    {
        private static readonly Guid IID_ILLDBServices = new Guid("2E6C569A-9E14-4DA4-9DFC-CDB73A532566");
        private static readonly Guid IID_ILLDBServices2 = new Guid("012F32F0-33BA-4E8E-BC01-037D382D8A5E");
        private static readonly Guid IID_SOSHostServices = new Guid("D13608FB-AD14-4B49-990A-80284F934C41");

        public IntPtr ILLDBServices { get; }

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
        /// Used by ISOSHostServices.GetSOSNETCoreCallbacks.
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
        }

        static SOSNetCoreCallbacks s_callbacks = new SOSNetCoreCallbacks {
            InitializeSymbolStoreDelegate = SymbolReader.InitializeSymbolStore,
            DisplaySymbolStoreDelegate = SymbolReader.DisplaySymbolStore,
            DisableSymbolStoreDelegate = SymbolReader.DisableSymbolStore,
            LoadNativeSymbolsDelegate = SymbolReader.LoadNativeSymbols,
            LoadSymbolsForModuleDelegate = SymbolReader.LoadSymbolsForModule,
            DisposeDelegate  = SymbolReader.Dispose,
            ResolveSequencePointDelegate = SymbolReader.ResolveSequencePoint,
            GetLineByILOffsetDelegate = SymbolReader.GetLineByILOffset,
            GetLocalVariableNameDelegate = SymbolReader.GetLocalVariableName,
            GetMetadataLocatorDelegate = MetadataHelper.GetMetadataLocator
        };

        static readonly string s_coreclrModuleName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "coreclr" : "libcoreclr.so";

        readonly SOSHost _sosHost;
        readonly IDataReader _dataReader;
        readonly ISOSHostContext _context;

        /// <summary>
        /// Create an instance of the service wrapper SOS uses.
        /// </summary>
        /// <param name="dataReader">clrmd data reader instance</param>
        /// <param name="context">sos hosting context</param>
        public LLDBServicesWrapper(SOSHost host, IDataReader dataReader, ISOSHostContext context)
        {
            _sosHost = host;
            _dataReader = dataReader;
            _context = context;

            VTableBuilder builder = AddInterface(IID_ILLDBServices, validate: false);

            builder.AddMethod(new GetCoreClrDirectoryDelegate(GetCoreClrDirectory));
            builder.AddMethod(new GetExpressionDelegate(GetExpression));
            builder.AddMethod(new VirtualUnwindDelegate(VirtualUnwind));
            builder.AddMethod(new SetExceptionCallbackDelegate(SetExceptionCallback));
            builder.AddMethod(new ClearExceptionCallbackDelegate(ClearExceptionCallback));

            builder.AddMethod(new GetInterruptDelegate(GetInterrupt));
            builder.AddMethod(new OutputVaListDelegate(OutputVaList));
            builder.AddMethod(new GetDebuggerTypeDelegate(GetDebuggerType));
            builder.AddMethod(new GetPageSizeDelegate(GetPageSize));
            builder.AddMethod(new GetExecutingProcessorTypeDelegate(GetExecutingProcessorType));
            builder.AddMethod(new ExecuteDelegate(Execute));
            builder.AddMethod(new GetLastEventInformationDelegate(GetLastEventInformation));
            builder.AddMethod(new DisassembleDelegate(Disassemble));

            builder.AddMethod(new GetContextStackTraceDelegate(GetContextStackTrace));
            builder.AddMethod(new ReadVirtualDelegate(ReadVirtual));
            builder.AddMethod(new WriteVirtualDelegate(WriteVirtual));

            builder.AddMethod(new GetSymbolOptionsDelegate(GetSymbolOptions));
            builder.AddMethod(new GetNameByOffsetDelegate(GetNameByOffset));
            builder.AddMethod(new GetNumberModulesDelegate(GetNumberModules));
            builder.AddMethod(new GetModuleByIndexDelegate(GetModuleByIndex));
            builder.AddMethod(new GetModuleByModuleNameDelegate(GetModuleByModuleName));
            builder.AddMethod(new GetModuleByOffsetDelegate(GetModuleByOffset));
            builder.AddMethod(new GetModuleNamesDelegate(GetModuleNames));
            builder.AddMethod(new GetLineByOffsetDelegate(GetLineByOffset));
            builder.AddMethod(new GetSourceFileLineOffsetsDelegate(GetSourceFileLineOffsets));
            builder.AddMethod(new FindSourceFileDelegate(FindSourceFile));

            builder.AddMethod(new GetCurrentProcessIdDelegate(GetCurrentProcessId));
            builder.AddMethod(new GetCurrentThreadIdDelegate(GetCurrentThreadId));
            builder.AddMethod(new SetCurrentThreadIdDelegate(SetCurrentThreadId));
            builder.AddMethod(new GetCurrentThreadSystemIdDelegate(GetCurrentThreadSystemId));
            builder.AddMethod(new GetThreadIdBySystemIdDelegate(GetThreadIdBySystemId));
            builder.AddMethod(new GetThreadContextByIdDelegate(GetThreadContextById));

            builder.AddMethod(new GetValueByNameDelegate(GetValueByName));
            builder.AddMethod(new GetInstructionOffsetDelegate(GetInstructionOffset));
            builder.AddMethod(new GetStackOffsetDelegate(GetStackOffset));
            builder.AddMethod(new GetFrameOffsetDelegate(GetFrameOffset));

            ILLDBServices = builder.Complete();

            builder = AddInterface(IID_ILLDBServices2, validate: false);
            builder.AddMethod(new LoadNativeSymbolsDelegate2(LoadNativeSymbols2));
            builder.AddMethod(new AddModuleSymbolDelegate(AddModuleSymbol));
            builder.Complete();

            builder = AddInterface(IID_SOSHostServices, validate: false);
            builder.AddMethod(new GetSOSNETCoreCallbacksDelegate(GetSOSNETCoreCallbacks));
            builder.Complete();

            AddRef();
        }

        #region ILLDBServices

        string GetCoreClrDirectory(
            IntPtr self)
        {
            foreach (ModuleInfo module in _dataReader.EnumerateModules())
            {
                if (string.Equals(Path.GetFileName(module.FileName), s_coreclrModuleName))
                {
                    return Path.GetDirectoryName(module.FileName) + Path.DirectorySeparatorChar;
                }
            }
            return null;
        }

        ulong GetExpression(
            IntPtr self,
            string text)
        {
            if (ulong.TryParse(text.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong result)) {
                return result;
            }
            return 0;
        }

        int VirtualUnwind(
            IntPtr self,
            uint threadId,
            uint contextSize,
            byte[] context)
        {
            return E_NOTIMPL;
        }

        int SetExceptionCallback(
            IntPtr self,
            PFN_EXCEPTION_CALLBACK callback)
        {
            return S_OK;
        }

        int ClearExceptionCallback(
            IntPtr self)
        {
            return S_OK;
        }

        int GetInterrupt(
            IntPtr self)
        {
            return _context.CancellationToken.IsCancellationRequested ? S_OK : E_FAIL;
        }

        int OutputVaList(
            IntPtr self,
            DEBUG_OUTPUT mask,
            string format,
            IntPtr va_list)
        {
            try
            {
                _context.Write(format);
            }
            catch (OperationCanceledException)
            {
                // ctrl-c interrupted the command
            }
            return S_OK;
        }

        int GetDebuggerType(
            IntPtr self,
            out DEBUG_CLASS debugClass,
            out DEBUG_CLASS_QUALIFIER qualifier)
        {
            debugClass = DEBUG_CLASS.USER_WINDOWS;
            qualifier = DEBUG_CLASS_QUALIFIER.USER_WINDOWS_DUMP;
            return S_OK;
        }

        int GetPageSize(
            IntPtr self,
            out uint size)
        {
            size = 4096;
            return S_OK;
        }

        int GetExecutingProcessorType(
            IntPtr self,
            out IMAGE_FILE_MACHINE type)
        {
            switch (_dataReader.GetArchitecture())
            {
                case Microsoft.Diagnostics.Runtime.Architecture.Amd64:
                    type = IMAGE_FILE_MACHINE.AMD64;
                    break;
                case Microsoft.Diagnostics.Runtime.Architecture.X86:
                    type = IMAGE_FILE_MACHINE.I386;
                    break;
                case Microsoft.Diagnostics.Runtime.Architecture.Arm:
                    type = IMAGE_FILE_MACHINE.ARM;
                    break;
                default:
                    type = IMAGE_FILE_MACHINE.UNKNOWN;
                    break;
            }
            return S_OK;
        }

        int Execute(
            IntPtr self,
            DEBUG_OUTCTL outputControl,
            string command,
            DEBUG_EXECUTE flags)
        {
            return E_NOTIMPL;
        }

        int GetLastEventInformation(
            IntPtr self,
            out uint type,
            out uint processId,
            out uint threadId,
            IntPtr extraInformation,
            uint extraInformationSize,
            out uint extraInformationUsed,
            string description,
            uint descriptionSize,
            out uint descriptionUsed)
        {
            // Should never be called. This exception will take down the program.
            throw new NotImplementedException("GetLastEventInformation");
        }

        int Disassemble(
            IntPtr self,
            ulong offset,
            DEBUG_DISASM flags,
            StringBuilder buffer,
            uint bufferSize,
            IntPtr pdisassemblySize,            // uint
            IntPtr pendOffset)                  // ulong
        {
            buffer.Clear();
            WriteUInt32(pdisassemblySize, 0);
            WriteUInt64(pendOffset, offset);
            return E_NOTIMPL;
        }

        int GetContextStackTrace(
            IntPtr self,
            IntPtr startContext,
            uint startContextSize,
            DEBUG_STACK_FRAME[] frames,
            uint framesSize,
            IntPtr frameContexts,
            uint frameContextsSize,
            uint frameContextsEntrySize,
            IntPtr pframesFilled)               // uint
        {
            // Don't fail, but always return 0 native frames so "clrstack -f" still prints the managed frames
            WriteUInt32(pframesFilled, 0);
            return S_OK;
        }

        int ReadVirtual(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            int bytesRequested,
            IntPtr pbytesRead)                  // uint
        {
            if (_dataReader.ReadMemory(address, buffer, bytesRequested, out int bytesRead))
            {
                WriteUInt32(pbytesRead, (uint)bytesRead);
                return S_OK;
            }
            return E_FAIL;
        }

        int WriteVirtual(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            uint bytesRequested,
            IntPtr pbytesWritten)
        {
            // This gets used by MemoryBarrier() calls in the dac, which really shouldn't matter what we do here.
            WriteUInt32(pbytesWritten, bytesRequested);
            return S_OK;
        }

        int GetSymbolOptions(
            IntPtr self,
            out SYMOPT options)
        {
            options = SYMOPT.LOAD_LINES; 
            return S_OK;
        }

        int GetNameByOffset(
            IntPtr self,
            ulong offset,
            StringBuilder nameBuffer,
            uint nameBufferSize,
            IntPtr pnameSize,                       // uint
            IntPtr pdisplacement)                   // ulong
        {
            nameBuffer.Clear();
            WriteUInt32(pnameSize, 0);
            WriteUInt64(pdisplacement, 0);
            return E_NOTIMPL;
        }

        int GetNumberModules(
            IntPtr self,
            out uint loaded,
            out uint unloaded)
        {
            loaded = (uint)_dataReader.EnumerateModules().Count();
            unloaded = 0;
            return S_OK;
        }

        int GetModuleByIndex(
            IntPtr self,
            uint index,
            out ulong baseAddress)
        {
            baseAddress = 0;
            try
            {
                ModuleInfo module = _dataReader.EnumerateModules().ElementAt((int)index);
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

        int GetModuleByModuleName(
            IntPtr self,
            string name,
            uint startIndex,
            IntPtr pIndex,
            out ulong baseAddress)
        {
            // The returned "index" is never used by SOS. Always passes startIndex = 0;
            Debug.Assert(pIndex == IntPtr.Zero);
            Debug.Assert(startIndex == 0);

            baseAddress = 0;
            foreach (ModuleInfo module in _dataReader.EnumerateModules())
            {
                if (string.Equals(Path.GetFileName(module.FileName), name))
                {
                    baseAddress = module.ImageBase;
                    return S_OK;
                }
            }
            return E_FAIL;
        }

        int GetModuleByOffset(
            IntPtr self,
            ulong offset,
            uint startIndex,
            IntPtr pindex,                          // uint
            IntPtr pbaseAddress)                    // ulong
        {
            WriteUInt32(pindex, 0);
            WriteUInt64(pbaseAddress, 0);
            return E_NOTIMPL;
        }

        int GetModuleNames(
            IntPtr self,
            uint index,
            ulong baseAddress,
            StringBuilder imageNameBuffer,
            uint imageNameBufferSize,
            IntPtr pimageNameSize,                  // uint
            StringBuilder moduleNameBuffer,
            uint ModuleNameBufferSize,
            IntPtr pmoduleNameSize,                 // uint
            StringBuilder loadedImageNameBuffer,
            uint loadedImageNameBufferSize,
            IntPtr ploadedImageNameSize)            // uint
        {
            WriteUInt32(pimageNameSize, 0);
            WriteUInt32(pmoduleNameSize, 0);
            WriteUInt32(ploadedImageNameSize, 0);
            return E_NOTIMPL;
        }

        int GetLineByOffset(
            IntPtr self,
            ulong offset,
            IntPtr pline,                            // uint
            StringBuilder fileBuffer,
            uint fileBufferSize,
            IntPtr pfileSize,                        // uint
            IntPtr pdisplacement)                    // ulong 
        {
            WriteUInt32(pline, 0);
            WriteUInt32(pfileSize, 0);
            WriteUInt64(pdisplacement, 0);
            return E_NOTIMPL;
        }

        int GetSourceFileLineOffsets(
            IntPtr self,
            string file,
            ulong[] buffer,
            uint bufferLines,
            IntPtr pfileLines)                      // uint
        {
            WriteUInt32(pfileLines, 0);
            return E_NOTIMPL;
        }

        int FindSourceFile(
            IntPtr self,
            uint startElement,
            string file,
            uint flags,
            IntPtr pfoundElement,                   // uint
            StringBuilder buffer,
            uint bufferSize,
            IntPtr pfoundSize)                      // uint
        {
            WriteUInt32(pfoundElement, 0);
            WriteUInt32(pfoundSize, 0);
            return E_NOTIMPL;
        }

        int GetCurrentProcessId(
            IntPtr self,
            out uint id)
        {
            id = 0;
            if (_dataReader is IDataReader2 dataReader2) {
                id = dataReader2.ProcessId;
            }
            return S_OK;
        }

        int GetCurrentThreadId(
            IntPtr self,
            out uint id)
        {
            return GetThreadIdBySystemId(self, (uint)_context.CurrentThreadId, out id);
        }

        int SetCurrentThreadId(
            IntPtr self,
            uint id)
        {
            try
            {
                unchecked {
                    _context.CurrentThreadId = (int)_dataReader.EnumerateAllThreads().ElementAt((int)id);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return E_FAIL;
            }
            return S_OK;
        }

        int GetCurrentThreadSystemId(
            IntPtr self,
            out uint sysId)
        {
            sysId = (uint)_context.CurrentThreadId;
            return S_OK;
        }

        int GetThreadIdBySystemId(
            IntPtr self,
            uint sysId,
            out uint id)
        {
            id = 0;
            if (sysId != 0)
            {
                foreach (uint s in _dataReader.EnumerateAllThreads())
                {
                    if (s == sysId) {
                        return S_OK;
                    }
                    id++;
                }
            }
            return E_FAIL;
        }

        int GetThreadContextById(
            IntPtr self,
            uint threadId,
            uint contextFlags,
            uint contextSize,
            IntPtr context)
        {
            if (_dataReader.GetThreadContext(threadId, contextFlags, contextSize, context)) {
                return S_OK;
            }
            return E_FAIL;
        }

        int GetValueByName(
            IntPtr self,
            string name,
            out ulong value)
        {
            return GetRegister(name, out value);
        }

        int GetInstructionOffset(
            IntPtr self,
            out ulong offset)
        {
            // TODO: Support other architectures
            return GetRegister("rip", out offset);
        }

        int GetStackOffset(
            IntPtr self,
            out ulong offset)
        {
            // TODO: Support other architectures
            return GetRegister("rsp", out offset);
        }

        int GetFrameOffset(
            IntPtr self,
            out ulong offset)
        {
            // TODO: Support other architectures
            return GetRegister("rbp", out offset);
        }

        #endregion 

        void WriteUInt32(IntPtr pointer, uint value)
        {
            if (pointer != IntPtr.Zero) {
                Marshal.WriteInt32(pointer, unchecked((int)value));
            }
        }

        void WriteUInt64(IntPtr pointer, ulong value)
        {
            if (pointer != IntPtr.Zero) {
                Marshal.WriteInt64(pointer, unchecked((long)value));
            }
        }

        // TODO: Support other architectures
        int GetRegister(string register, out ulong value)
        {
            value = 0;
            int hr = GetCurrentThreadSystemId(IntPtr.Zero, out uint threadId);
            if (hr != 0) {
                return hr;
            }
            byte[] buffer = new byte[AMD64Context.Size];
            if (!_dataReader.GetThreadContext(threadId, uint.MaxValue, (uint)AMD64Context.Size, buffer))
            {
                return E_FAIL;
            }
            fixed (byte* ptr = buffer)
            {
                AMD64Context* context = (AMD64Context*)ptr;
                switch (register.ToLower())
                {
                    case "rax":
                        value = context->Rax;
                        break;
                    case "rbx":
                        value = context->Rbx;
                        break;
                    case "rcx":
                        value = context->Rcx;
                        break;
                    case "rdx":
                        value = context->Rdx;
                        break;
                    case "rsi":
                        value = context->Rsi;
                        break;
                    case "rdi":
                        value = context->Rdi;
                        break;
                    case "r8":
                        value = context->R8;
                        break;
                    case "r9":
                        value = context->R9;
                        break;
                    case "r10":
                        value = context->R10;
                        break;
                    case "r11":
                        value = context->R11;
                        break;
                    case "r12":
                        value = context->R12;
                        break;
                    case "r13":
                        value = context->R13;
                        break;
                    case "r14":
                        value = context->R14;
                        break;
                    case "r15":
                        value = context->R15;
                        break;
                    case "rip":
                        value = context->Rip;
                        break;
                    case "rsp":
                        value = context->Rsp;
                        break;
                    case "rbp":
                        value = context->Rbp;
                        break;
                    default:
                        return E_FAIL;
                }
            }
            return S_OK;
        }

        #region ILLDBServices2

        int LoadNativeSymbols2(
            IntPtr self,
            bool runtimeOnly,
            ModuleLoadCallback callback)
        {
            foreach (ModuleInfo module in _dataReader.EnumerateModules())
            {
                callback(IntPtr.Zero, module.FileName, module.ImageBase, unchecked((int)module.FileSize));
            }
            return S_OK;
        }

        int AddModuleSymbol(
            IntPtr self,
            IntPtr parameter,
            string symbolFilename)
        {
            return S_OK;
        }

        #endregion

        #region ISOSHostServices

        int GetSOSNETCoreCallbacks(
            IntPtr self,
            int version,
            IntPtr pCallbacks)
        {
            if (version < 1)
            {
                return E_FAIL;
            }
            try
            {
                Marshal.StructureToPtr(s_callbacks, pCallbacks, false);
            }
            catch (ArgumentException)
            {
                return E_FAIL;
            }
            return S_OK;
        }

        #endregion 

        #region ILLDBServices delegates

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.LPStr)]
        private delegate string GetCoreClrDirectoryDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate ulong GetExpressionDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string text);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int VirtualUnwindDelegate(
            IntPtr self,
            uint threadId,
            uint contextSize,
            byte[] context);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int PFN_EXCEPTION_CALLBACK(LLDBServicesWrapper services);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetExceptionCallbackDelegate(
            IntPtr self,
            PFN_EXCEPTION_CALLBACK callback);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ClearExceptionCallbackDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetInterruptDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int OutputVaListDelegate(
            IntPtr self,
            DEBUG_OUTPUT mask,
            [In, MarshalAs(UnmanagedType.LPStr)] string format,
            IntPtr va_list);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDebuggerTypeDelegate(
            IntPtr self,
            out DEBUG_CLASS debugClass,
            out DEBUG_CLASS_QUALIFIER qualifier);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetPageSizeDelegate(
            IntPtr self,
            out uint size);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetExecutingProcessorTypeDelegate(
            IntPtr self,
            out IMAGE_FILE_MACHINE type);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ExecuteDelegate(
            IntPtr self,
            DEBUG_OUTCTL outputControl,
            [In, MarshalAs(UnmanagedType.LPStr)] string command,
            DEBUG_EXECUTE flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetLastEventInformationDelegate(
            IntPtr self,
            out uint type,
            out uint processId,
            out uint threadId,
            IntPtr extraInformation,
            uint extraInformationSize,
            out uint extraInformationUsed,
            [In][MarshalAs(UnmanagedType.LPStr)] string description,
            uint descriptionSize,
            out uint descriptionUsed);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DisassembleDelegate(
            IntPtr self,
            ulong offset,
            DEBUG_DISASM flags,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder buffer,
            uint bufferSize,
            IntPtr pdisassemblySize,
            IntPtr pendOffset);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetContextStackTraceDelegate(
            IntPtr self,
            IntPtr startContext,
            uint startContextSize,
            [Out, MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME[] frames,
            uint framesSize,
            IntPtr frameContexts,
            uint frameContextsSize,
            uint frameContextsEntrySize,
            IntPtr pframesFilled);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReadVirtualDelegate(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            int bytesRequested,
            IntPtr pbytesRead);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int WriteVirtualDelegate(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            uint bytesRequested,
            IntPtr pbytesWritten);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetSymbolOptionsDelegate(
            IntPtr self,
            out SYMOPT options);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetNameByOffsetDelegate(
            IntPtr self,
            ulong offset,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder nameBuffer,
            uint nameBufferSize,
            IntPtr pnameSize,
            IntPtr pdisplacement);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetNumberModulesDelegate(
            IntPtr self,
            out uint loaded,
            out uint unloaded);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetModuleByIndexDelegate(
            IntPtr self,
            uint index,
            out ulong baseAddress);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetModuleByModuleNameDelegate(
            IntPtr self,
            [In, MarshalAs(UnmanagedType.LPStr)] string name,
            uint startIndex,
            IntPtr index,
            out ulong baseAddress);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetModuleByOffsetDelegate(
            IntPtr self,
            ulong offset,
            uint startIndex,
            IntPtr pindex,
            IntPtr pbaseAddress);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetModuleNamesDelegate(
            IntPtr self,
            uint index,
            ulong baseAddress,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder imageNameBuffer,
            uint imageNameBufferSize,
            IntPtr pimageNameSize,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder moduleNameBuffer,
            uint ModuleNameBufferSize,
            IntPtr pmoduleNameSize,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder loadedImageNameBuffer,
            uint loadedImageNameBufferSize,
            IntPtr ploadedImageNameSize);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetLineByOffsetDelegate(
            IntPtr self,
            ulong offset,
            IntPtr line,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder fileBuffer,
            uint fileBufferSize,
            IntPtr fileSize,
            IntPtr displacement);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetSourceFileLineOffsetsDelegate(
            IntPtr self,
            [In, MarshalAs(UnmanagedType.LPStr)] string file,
            [Out, MarshalAs(UnmanagedType.LPArray)] ulong[] buffer,
            uint bufferLines,
            IntPtr fileLines);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int FindSourceFileDelegate(
            IntPtr self,
            uint startElement,
            [In, MarshalAs(UnmanagedType.LPStr)] string file,
            uint flags,
            IntPtr foundElement,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder buffer,
            uint bufferSize,
            IntPtr foundSize);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetCurrentProcessIdDelegate(
            IntPtr self,
            out uint id);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetCurrentThreadIdDelegate(
            IntPtr self,
            out uint id);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetCurrentThreadIdDelegate(
            IntPtr self,
            uint id);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetCurrentThreadSystemIdDelegate(
            IntPtr self,
            out uint sysId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetThreadIdBySystemIdDelegate(
            IntPtr self,
            uint sysId,
            out uint id);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetThreadContextByIdDelegate(
            IntPtr self,
            uint threadId,
            uint contextFlags,
            uint contextSize,
            IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetValueByNameDelegate(
            IntPtr self,
            [In, MarshalAs(UnmanagedType.LPStr)] string name,
            out ulong value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetInstructionOffsetDelegate(
            IntPtr self,
            out ulong offset);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetStackOffsetDelegate(
            IntPtr self,
            out ulong offset);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetFrameOffsetDelegate(
            IntPtr self,
            out ulong offset);

        #endregion

        #region ILLDBServices2 delegates

        /// <summary>
        /// The LoadNativeSymbolsDelegate2 callback
        /// </summary>
        public delegate void ModuleLoadCallback(
            IntPtr parameter,
            [MarshalAs(UnmanagedType.LPStr)] string moduleFilePath,
            ulong moduleAddress,
            int moduleSize);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int LoadNativeSymbolsDelegate2(
            IntPtr self,
            bool runtimeOnly,
            ModuleLoadCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int AddModuleSymbolDelegate(
            IntPtr self,
            IntPtr parameter,
            [MarshalAs(UnmanagedType.LPStr)] string symbolFilename);

        #endregion

        #region ISOSHostServices delegates

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetSOSNETCoreCallbacksDelegate(
            IntPtr self,
            int version,
            IntPtr pCallbacks);

        #endregion 
    }
}