// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS
{
    internal unsafe class LLDBServices : COMCallableIUnknown
    {
        private static readonly Guid IID_ILLDBServices = new Guid("2E6C569A-9E14-4DA4-9DFC-CDB73A532566");
        private static readonly Guid IID_ILLDBServices2 = new Guid("012F32F0-33BA-4E8E-BC01-037D382D8A5E");

        public IntPtr ILLDBServices { get; }

        readonly SOSHost _soshost;

        /// <summary>
        /// Create an instance of the service wrapper SOS uses.
        /// </summary>
        /// <param name="soshost">SOS host instance</param>
        public LLDBServices(SOSHost soshost)
        {
            _soshost = soshost;

            VTableBuilder builder = AddInterface(IID_ILLDBServices, validate: false);

            builder.AddMethod(new GetCoreClrDirectoryDelegate(GetCoreClrDirectory));
            builder.AddMethod(new GetExpressionDelegate((self, expression) => SOSHost.GetExpression(expression)));
            builder.AddMethod(new VirtualUnwindDelegate(VirtualUnwind));
            builder.AddMethod(new SetExceptionCallbackDelegate(SetExceptionCallback));
            builder.AddMethod(new ClearExceptionCallbackDelegate(ClearExceptionCallback));

            builder.AddMethod(new GetInterruptDelegate(soshost.GetInterrupt));
            builder.AddMethod(new OutputVaListDelegate(soshost.OutputVaList));
            builder.AddMethod(new GetDebuggeeTypeDelegate(soshost.GetDebuggeeType));
            builder.AddMethod(new GetPageSizeDelegate(soshost.GetPageSize));
            builder.AddMethod(new GetExecutingProcessorTypeDelegate(soshost.GetExecutingProcessorType));
            builder.AddMethod(new ExecuteDelegate(soshost.Execute));
            builder.AddMethod(new GetLastEventInformationDelegate(soshost.GetLastEventInformation));
            builder.AddMethod(new DisassembleDelegate(soshost.Disassemble));

            builder.AddMethod(new GetContextStackTraceDelegate(GetContextStackTrace));
            builder.AddMethod(new ReadVirtualDelegate(soshost.ReadVirtual));
            builder.AddMethod(new WriteVirtualDelegate(soshost.WriteVirtual));

            builder.AddMethod(new GetSymbolOptionsDelegate(soshost.GetSymbolOptions));
            builder.AddMethod(new GetNameByOffsetDelegate(soshost.GetNameByOffset));
            builder.AddMethod(new GetNumberModulesDelegate(soshost.GetNumberModules));
            builder.AddMethod(new GetModuleByIndexDelegate(soshost.GetModuleByIndex));
            builder.AddMethod(new GetModuleByModuleNameDelegate(soshost.GetModuleByModuleName));
            builder.AddMethod(new GetModuleByOffsetDelegate(soshost.GetModuleByOffset));
            builder.AddMethod(new GetModuleNamesDelegate(soshost.GetModuleNames));
            builder.AddMethod(new GetLineByOffsetDelegate(soshost.GetLineByOffset));
            builder.AddMethod(new GetSourceFileLineOffsetsDelegate(soshost.GetSourceFileLineOffsets));
            builder.AddMethod(new FindSourceFileDelegate(soshost.FindSourceFile));

            builder.AddMethod(new GetCurrentProcessIdDelegate(soshost.GetCurrentProcessId));
            builder.AddMethod(new GetCurrentThreadIdDelegate(soshost.GetCurrentThreadId));
            builder.AddMethod(new SetCurrentThreadIdDelegate(soshost.SetCurrentThreadId));
            builder.AddMethod(new GetCurrentThreadSystemIdDelegate(soshost.GetCurrentThreadSystemId));
            builder.AddMethod(new GetThreadIdBySystemIdDelegate(soshost.GetThreadIdBySystemId));
            builder.AddMethod(new GetThreadContextByIdDelegate(GetThreadContextById));

            builder.AddMethod(new GetValueByNameDelegate(GetValueByName));
            builder.AddMethod(new GetInstructionOffsetDelegate(soshost.GetInstructionOffset));
            builder.AddMethod(new GetStackOffsetDelegate(soshost.GetStackOffset));
            builder.AddMethod(new GetFrameOffsetDelegate(soshost.GetFrameOffset));

            ILLDBServices = builder.Complete();

            builder = AddInterface(IID_ILLDBServices2, validate: false);
            builder.AddMethod(new LoadNativeSymbolsDelegate2(LoadNativeSymbols2));
            builder.AddMethod(new AddModuleSymbolDelegate(AddModuleSymbol));
            builder.AddMethod(new GetModuleInfoDelegate(GetModuleInfo));
            builder.AddMethod(new GetModuleVersionInformationDelegate(soshost.GetModuleVersionInformation));
            builder.Complete();

            AddRef();
        }

        #region ILLDBServices

        string GetCoreClrDirectory(
            IntPtr self)
        {
            if (_soshost.AnalyzeContext.RuntimeModuleDirectory == null)
            {
                foreach (ModuleInfo module in _soshost.DataReader.EnumerateModules())
                {
                    if (SOSHost.IsCoreClrRuntimeModule(module))
                    {
                        _soshost.AnalyzeContext.RuntimeModuleDirectory = Path.GetDirectoryName(module.FileName) + Path.DirectorySeparatorChar;
                    }
                }
            }
            return _soshost.AnalyzeContext.RuntimeModuleDirectory;
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

        int GetContextStackTrace(
            IntPtr self,
            IntPtr startContext,
            uint startContextSize,
            DEBUG_STACK_FRAME[] frames,
            uint framesSize,
            IntPtr frameContexts,
            uint frameContextsSize,
            uint frameContextsEntrySize,
            uint* framesFilled)
        {
            // Don't fail, but always return 0 native frames so "clrstack -f" still prints the managed frames
            SOSHost.Write(framesFilled);
            return S_OK;
        }

        int GetThreadContextById(
            IntPtr self,
            uint threadId,
            uint contextFlags,
            uint contextSize,
            IntPtr context)
        {
            if (_soshost.DataReader.GetThreadContext(threadId, contextFlags, contextSize, context)) {
                return S_OK;
            }
            return E_FAIL;
        }

        int GetValueByName(
            IntPtr self,
            string name,
            out UIntPtr value)
        {
            int hr = _soshost.GetRegister(name, out ulong register);
            value = new UIntPtr(register);
            return hr;
        }

        #endregion 

        #region ILLDBServices2

        int LoadNativeSymbols2(
            IntPtr self,
            bool runtimeOnly,
            ModuleLoadCallback callback)
        {
            foreach (ModuleInfo module in _soshost.DataReader.EnumerateModules())
            {
                if (runtimeOnly)
                {
                    if (SOSHost.IsCoreClrRuntimeModule(module))
                    {
                        callback(IntPtr.Zero, module.FileName, module.ImageBase, unchecked((int)module.FileSize));
                        break;
                    }
                }
                else
                {
                    callback(IntPtr.Zero, module.FileName, module.ImageBase, unchecked((int)module.FileSize));
                }
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

        unsafe int GetModuleInfo(
            IntPtr self,
            uint index,
            ulong *moduleBase,
            ulong *moduleSize)
        {
            try
            {
                ModuleInfo module = _soshost.DataReader.EnumerateModules().ElementAt((int)index);
                if (module == null) {
                    return E_FAIL;
                }
                SOSHost.Write(moduleBase, module.ImageBase);
                SOSHost.Write(moduleSize, module.FileSize);
            }
            catch (ArgumentOutOfRangeException)
            {
                return E_FAIL;
            }
            return S_OK;
        }

        #endregion

        #region ILLDBServices delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.LPStr)]
        private delegate string GetCoreClrDirectoryDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate UIntPtr GetExpressionDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string text);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int VirtualUnwindDelegate(
            IntPtr self,
            uint threadId,
            uint contextSize,
            byte[] context);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int PFN_EXCEPTION_CALLBACK(LLDBServices services);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetExceptionCallbackDelegate(
            IntPtr self,
            PFN_EXCEPTION_CALLBACK callback);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ClearExceptionCallbackDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetInterruptDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputVaListDelegate(
            IntPtr self,
            DEBUG_OUTPUT mask,
            [In, MarshalAs(UnmanagedType.LPStr)] string format,
            IntPtr va_list);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetDebuggeeTypeDelegate(
            IntPtr self,
            [Out] DEBUG_CLASS* Class,
            [Out] DEBUG_CLASS_QUALIFIER* Qualifier);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetPageSizeDelegate(
            IntPtr self,
            [Out] uint* size);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetExecutingProcessorTypeDelegate(
            IntPtr self,
            [Out] IMAGE_FILE_MACHINE* type);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ExecuteDelegate(
            IntPtr self,
            DEBUG_OUTCTL outputControl,
            [In, MarshalAs(UnmanagedType.LPStr)] string command,
            DEBUG_EXECUTE flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetLastEventInformationDelegate(
            IntPtr self,
            [Out] DEBUG_EVENT* type,
            [Out] uint* processId,
            [Out] uint* threadId,
            [In] IntPtr extraInformation,
            [In] uint extraInformationSize,
            [Out] uint* extraInformationUsed,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder description,
            [In] uint descriptionSize,
            [Out] uint* descriptionUsed);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int DisassembleDelegate(
            IntPtr self,
            [In] ulong offset,
            [In] DEBUG_DISASM flags,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder buffer,
            [In] uint bufferSize,
            [Out] uint* disassemblySize,
            [Out] ulong* endOffset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetContextStackTraceDelegate(
            IntPtr self,
            IntPtr startContext,
            uint startContextSize,
            [Out, MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME[] frames,
            uint framesSize,
            IntPtr frameContexts,
            uint frameContextsSize,
            uint frameContextsEntrySize,
            uint* pframesFilled);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadVirtualDelegate(
            IntPtr self,
            [In] ulong address,
            IntPtr buffer,
            [In] uint bufferSize,
            [Out] uint* bytesRead);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteVirtualDelegate(
            IntPtr self,
            [In] ulong address,
            IntPtr buffer,
            [In] uint bufferSize,
            [Out] uint* bytesWritten);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolOptionsDelegate(
            IntPtr self,
            out SYMOPT options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNameByOffsetDelegate(
            IntPtr self,
            [In] ulong offset,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder nameBuffer,
            [In] uint nameBufferSize,
            [Out] uint* nameSize,
            [Out] ulong* displacement);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNumberModulesDelegate(
            IntPtr self,
            out uint loaded,
            out uint unloaded);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleByIndexDelegate(
            IntPtr self,
            uint index,
            out ulong baseAddress);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleByModuleNameDelegate(
            IntPtr self,
            [In, MarshalAs(UnmanagedType.LPStr)] string name,
            uint startIndex,
            uint* index,
            ulong* baseAddress);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleByOffsetDelegate(
            IntPtr self,
            ulong offset,
            uint startIndex,
            uint* index,
            ulong* baseAddress);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleNamesDelegate(
            IntPtr self,
            uint index,
            ulong baseAddress,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder imageNameBuffer,
            uint imageNameBufferSize,
            uint* imageNameSize,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder moduleNameBuffer,
            uint ModuleNameBufferSize,
            uint* moduleNameSize,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder loadedImageNameBuffer,
            uint loadedImageNameBufferSize,
            uint* loadedImageNameSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetLineByOffsetDelegate(
            IntPtr self,
            ulong offset,
            uint* line,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder fileBuffer,
            uint fileBufferSize,
            uint* fileSize,
            ulong* displacement);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourceFileLineOffsetsDelegate(
            IntPtr self,
            [In, MarshalAs(UnmanagedType.LPStr)] string file,
            [Out, MarshalAs(UnmanagedType.LPArray)] ulong[] buffer,
            uint bufferLines,
            uint* fileLines);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int FindSourceFileDelegate(
            IntPtr self,
            uint startElement,
            [In, MarshalAs(UnmanagedType.LPStr)] string file,
            DEBUG_FIND_SOURCE flags,
            uint* foundElement,
            [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder buffer,
            uint bufferSize,
            uint* foundSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentProcessIdDelegate(
            IntPtr self,
            out uint id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentThreadIdDelegate(
            IntPtr self,
            [Out] out uint id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetCurrentThreadIdDelegate(
            IntPtr self,
            [In] uint id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentThreadSystemIdDelegate(
            IntPtr self,
            [Out] out uint sysId);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetThreadIdBySystemIdDelegate(
            IntPtr self,
            [In] uint sysId,
            [Out] out uint id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetThreadContextByIdDelegate(
            IntPtr self,
            uint threadId,
            uint contextFlags,
            uint contextSize,
            IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetValueByNameDelegate(
            IntPtr self,
            [In, MarshalAs(UnmanagedType.LPStr)] string name,
            out UIntPtr value);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetInstructionOffsetDelegate(
            IntPtr self,
            [Out] out ulong offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetStackOffsetDelegate(
            IntPtr self,
            [Out] out ulong offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetFrameOffsetDelegate(
            IntPtr self,
            [Out] out ulong offset);

        #endregion

        #region ILLDBServices2 delegates

        /// <summary>
        /// The LoadNativeSymbolsDelegate2 callback
        /// </summary>
        public delegate void ModuleLoadCallback(
            IntPtr parameter,
            [In, MarshalAs(UnmanagedType.LPStr)] string moduleFilePath,
            [In] ulong moduleAddress,
            [In] int moduleSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int LoadNativeSymbolsDelegate2(
            IntPtr self,
            [In] bool runtimeOnly,
            [In] ModuleLoadCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AddModuleSymbolDelegate(
            IntPtr self,
            [In] IntPtr parameter,
            [In, MarshalAs(UnmanagedType.LPStr)] string symbolFilename);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private unsafe delegate int GetModuleInfoDelegate(
            IntPtr self,
            [In] uint index,
            [Out] ulong *moduleBase,
            [Out] ulong *moduleSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleVersionInformationDelegate(
            IntPtr self,
            [In] uint Index,
            [In] ulong Base,
            [In][MarshalAs(UnmanagedType.LPStr)] string Item,
            [Out] byte* Buffer,
            [In] uint BufferSize,
            [Out] uint* VerInfoSize);

        #endregion
    }
}