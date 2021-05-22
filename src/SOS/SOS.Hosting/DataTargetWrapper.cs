// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace SOS.Hosting
{
    internal sealed unsafe class DataTargetWrapper : COMCallableIUnknown
    {
        private static readonly Guid IID_ICLRDataTarget = new Guid("3E11CCEE-D08B-43e5-AF01-32717A64DA03");
        private static readonly Guid IID_ICLRDataTarget2 = new Guid("6d05fae3-189c-4630-a6dc-1c251e1c01ab");
        private static readonly Guid IID_ICLRDataTarget4 = new Guid("E799DC06-E099-4713-BDD9-906D3CC02CF2");
        private static readonly Guid IID_ICLRMetadataLocator = new Guid("aa8fa804-bc05-4642-b2c5-c353ed22fc63");
        private static readonly Guid IID_ICLRRuntimeLocator = new Guid("b760bf44-9377-4597-8be7-58083bdc5146");

        // For ClrMD's magic hand shake
        private const ulong MagicCallbackConstant = 0x43;

        private readonly IRuntime _runtime;
        private readonly IContextService _contextService;
        private readonly ISymbolService _symbolService;
        private readonly IMemoryService _memoryService;
        private readonly IThreadService _threadService;
        private readonly IModuleService _moduleService;
        private readonly IThreadUnwindService _threadUnwindService;
        private readonly IRemoteMemoryService _remoteMemoryService;
        private readonly ulong _ignoreAddressBitsMask;

        public IntPtr IDataTarget { get; }

        public DataTargetWrapper(IServiceProvider services, IRuntime runtime)
        {
            Debug.Assert(services != null);
            Debug.Assert(runtime != null);
            _runtime = runtime;
            _contextService = services.GetService<IContextService>();
            _symbolService = services.GetService<ISymbolService>();
            _memoryService = services.GetService<IMemoryService>();
            _threadService = services.GetService<IThreadService>();
            _threadUnwindService = services.GetService<IThreadUnwindService>();
            _moduleService = services.GetService<IModuleService>();
            _remoteMemoryService = services.GetService<IRemoteMemoryService>();
            _ignoreAddressBitsMask = _memoryService.SignExtensionMask();

            VTableBuilder builder = AddInterface(IID_ICLRDataTarget, false);
            AddDataTarget(builder);
            IDataTarget = builder.Complete();

            builder = AddInterface(IID_ICLRDataTarget2, false);
            AddDataTarget2(builder);
            builder.Complete();

            builder = AddInterface(IID_ICLRDataTarget4, validate: false);
            builder.AddMethod(new VirtualUnwindDelegate(VirtualUnwind));
            builder.Complete();

            builder = AddInterface(IID_ICLRMetadataLocator, false);
            builder.AddMethod(new GetMetadataDelegate(GetMetadata));
            builder.Complete();

            builder = AddInterface(IID_ICLRRuntimeLocator, false);
            builder.AddMethod(new GetRuntimeBaseDelegate(GetRuntimeBase));
            builder.Complete();

            AddRef();
        }

        void AddDataTarget(VTableBuilder builder)
        {
            builder.AddMethod(new GetMachineTypeDelegate(GetMachineType));
            builder.AddMethod(new GetPointerSizeDelegate(GetPointerSize));
            builder.AddMethod(new GetImageBaseDelegate(GetImageBase));
            builder.AddMethod(new ReadVirtualDelegate(ReadVirtual));
            builder.AddMethod(new WriteVirtualDelegate(WriteVirtual));
            builder.AddMethod(new GetTLSValueDelegate(GetTLSValue));
            builder.AddMethod(new SetTLSValueDelegate(SetTLSValue));
            builder.AddMethod(new GetCurrentThreadIDDelegate(GetCurrentThreadID));
            builder.AddMethod(new GetThreadContextDelegate(GetThreadContext));
            builder.AddMethod(new SetThreadContextDelegate(SetThreadContext));
            builder.AddMethod(new RequestDelegate(Request));
        }

        void AddDataTarget2(VTableBuilder builder)
        {
            AddDataTarget(builder);
            builder.AddMethod(new AllocVirtualDelegate(AllocVirtual));
            builder.AddMethod(new FreeVirtualDelegate(FreeVirtual));
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("DataTargetWrapper.Destroy");
        }

        #region ICLRDataTarget

        private int GetMachineType(
            IntPtr self,
            out IMAGE_FILE_MACHINE machineType)
        {
            ITarget target = _runtime.Target;
            Debug.Assert(target != null);
            machineType = target.Architecture switch
            {
                Architecture.X64 => IMAGE_FILE_MACHINE.AMD64,
                Architecture.X86 => IMAGE_FILE_MACHINE.I386,
                Architecture.Arm => IMAGE_FILE_MACHINE.THUMB2,
                Architecture.Arm64 => IMAGE_FILE_MACHINE.ARM64,
                _ => IMAGE_FILE_MACHINE.UNKNOWN,
            };
            return HResult.S_OK;
        }

        private int GetPointerSize(
            IntPtr self,
            out int pointerSize)
        {
            pointerSize = _memoryService.PointerSize;
            return HResult.S_OK;
        }

        private int GetImageBase(
            IntPtr self,
            string imagePath,
            out ulong baseAddress)
        {
            IModule module = _moduleService.GetModuleFromModuleName(imagePath).FirstOrDefault();
            if (module != null)
            {
                baseAddress = module.ImageBase;
                return HResult.S_OK;
            }
            baseAddress = 0;
            return HResult.E_FAIL;
        }

        private int ReadVirtual(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            uint bytesRequested,
            uint* pbytesRead)
        {
            Debug.Assert(address != MagicCallbackConstant);
            int read = 0;
            if (bytesRequested > 0)
            {
                address &= _ignoreAddressBitsMask;
                if (!_memoryService.ReadMemory(address, buffer, unchecked((int)bytesRequested), out read))
                {
                    Trace.TraceError("DataTargetWrapper.ReadVirtual FAILED address {0:X16} size {1:X8}", address, bytesRequested);
                    SOSHost.Write(pbytesRead);
                    return HResult.E_FAIL;
                }
            }
            SOSHost.Write(pbytesRead, (uint)read);
            return HResult.S_OK;
        }

        private int WriteVirtual(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            uint bytesRequested,
            uint* bytesWritten)
        {
            address &= _ignoreAddressBitsMask;
            if (!_memoryService.WriteMemory(address, new Span<byte>(buffer.ToPointer(), unchecked((int)bytesRequested)), out int written))
            {
                SOSHost.Write(bytesWritten);
                return HResult.E_FAIL;
            }
            SOSHost.Write(bytesWritten, (uint)written);
            return HResult.S_OK;
        }

        private int GetTLSValue(
            IntPtr self,
            uint threadId,
            uint index,
            ulong* value)
        {
            return HResult.E_NOTIMPL;
        }

        private int SetTLSValue(
            IntPtr self,
            uint threadId,
            uint index,
            ulong value)
        {
            return HResult.E_NOTIMPL;
        }

        private int GetCurrentThreadID(
            IntPtr self,
            out uint threadId)
        {
            uint? id = _contextService.GetCurrentThread()?.ThreadId;
            if (id.HasValue)
            {
                threadId = id.Value;
                return HResult.S_OK;
            }
            threadId = 0;
            return HResult.E_FAIL;
        }

        private int GetThreadContext(
            IntPtr self,
            uint threadId,
            uint contextFlags,
            int contextSize,
            IntPtr context)
        {
            byte[] registerContext;
            try
            {
                registerContext = _threadService.GetThreadFromId(threadId).GetThreadContext();
            }
            catch (DiagnosticsException)
            {
                Trace.TraceError($"DataTargetWrapper.GetThreadContext({threadId:X8}) FAILED");
                return HResult.E_FAIL;
            }
            try
            {
                Marshal.Copy(registerContext, 0, context, contextSize);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException || ex is ArgumentNullException)
            {
                Trace.TraceError($"DataTargetWrapper.GetThreadContext Marshal.Copy FAILED {ex}");
                return HResult.E_INVALIDARG;
            }
            return HResult.S_OK;
        }

        private int SetThreadContext(
            IntPtr self,
            uint threadId,
            int contextSize,
            IntPtr context)
        {
            return HResult.E_NOTIMPL;
        }

        private int Request(
            IntPtr self,
            uint reqCode,
            uint inBufferSize,
            IntPtr inBuffer,
            IntPtr outBufferSize,
            IntPtr* outBuffer)
        {
            return HResult.E_NOTIMPL;
        }

        #endregion

        #region ICLRDataTarget2

        private int AllocVirtual(
            IntPtr self,
            ulong address,
            uint size,
            uint typeFlags,
            uint protectFlags,
            ulong* buffer)
        {
            if (_remoteMemoryService == null) {
                return HResult.E_NOTIMPL;
            }
            if (!_remoteMemoryService.AllocateMemory(address, size, typeFlags, protectFlags, out ulong remoteAddress)) {
                return HResult.E_FAIL;
            }
            SOSHost.Write(buffer, remoteAddress);
            return HResult.S_OK;
        }

        private int FreeVirtual(
            IntPtr self,
            ulong address,
            uint size,
            uint typeFlags)
        {
            if (_remoteMemoryService == null) {
                return HResult.E_NOTIMPL;
            }
            if (!_remoteMemoryService.FreeMemory(address, size, typeFlags)) {
                return HResult.E_FAIL;
            }
            return HResult.S_OK;
        }

        #endregion

        #region ICLRDataTarget4

        private int VirtualUnwind(
            IntPtr self,
            uint threadId,
            uint contextSize,
            byte[] context)
        {
            try
            {
                if (_threadUnwindService == null) {
                    return HResult.E_NOTIMPL;
                }
                return _threadUnwindService.Unwind(threadId, contextSize, context);
            }
            catch (DiagnosticsException)
            {
                return HResult.E_INVALIDARG;
            }
        }

        #endregion

        #region ICLRMetadataLocator

        private int GetMetadata(
            IntPtr self,
            string fileName,
            uint imageTimestamp,
            uint imageSize,
            byte[] mvid,
            uint mdRva,
            uint flags,
            uint bufferSize,
            IntPtr buffer,
            IntPtr dataSize)
        {
            return _symbolService.GetMetadataLocator(fileName, imageTimestamp, imageSize, mvid, mdRva, flags, bufferSize, buffer, dataSize);
        }

        #endregion

        #region ICLRRuntimeLocator

        private int GetRuntimeBase(
            IntPtr self,
            out ulong address)
        {
            address = _runtime.RuntimeModule.ImageBase;
            return HResult.S_OK;
        }

        #endregion

        #region ICLRDataTarget delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetMachineTypeDelegate(
            [In] IntPtr self,
            [Out] out IMAGE_FILE_MACHINE machineType);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetPointerSizeDelegate(
            [In] IntPtr self,
            [Out] out int pointerSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetImageBaseDelegate(
            [In] IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string imagePath,
            [Out] out ulong baseAddress);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadVirtualDelegate(
            [In] IntPtr self,
            [In] ulong address,
            [In] IntPtr buffer,
            [In] uint bytesRequested,
            [Out] uint* bytesRead);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteVirtualDelegate(
            [In] IntPtr self,
            [In] ulong address,
            [In] IntPtr buffer,
            [In] uint bytesRequested,
            [Out] uint* bytesWritten);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetTLSValueDelegate(
            [In] IntPtr self,
            [In] uint threadId,
            [In] uint index,
            [Out] ulong* value);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetTLSValueDelegate(
            [In] IntPtr self,
            [In] uint threadId,
            [In] uint index,
            [In] ulong value);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentThreadIDDelegate(
            [In] IntPtr self,
            [Out] out uint threadId);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetThreadContextDelegate(
            [In] IntPtr self,
            [In] uint threadId,
            [In] uint contextFlags,
            [In] int contextSize,
            [Out] IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetThreadContextDelegate(
            [In] IntPtr self,
            [In] uint threadId,
            [In] int contextSize,
            [In] IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RequestDelegate(
            [In] IntPtr self,
            [In] uint reqCode,
            [In] uint inBufferSize,
            [In] IntPtr inBuffer,
            [In] IntPtr outBufferSize,
            [Out] IntPtr* outBuffer);

        #endregion

        #region ICLRDataTarget2 delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AllocVirtualDelegate(
            [In] IntPtr self,
            [In] ulong address,
            [In] uint size,
            [In] uint typeFlags,
            [In] uint protectFlags,
            [Out] ulong* buffer);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int FreeVirtualDelegate(
            [In] IntPtr self,
            [In] ulong address,
            [In] uint size,
            [In] uint typeFlags);

        #endregion

        #region ICLRDataTarget4 delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int VirtualUnwindDelegate(
            [In] IntPtr self,
            [In] uint threadId,
            [In] uint contextSize,
            [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] context);

        #endregion

        #region ICLRMetadataLocator delegate

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetMetadataDelegate(
            [In] IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string fileName,
            [In] uint imageTimestamp,
            [In] uint imageSize, 
            [In] [MarshalAs(UnmanagedType.LPArray, SizeConst = 16)] byte[] mvid,
            [In] uint mdRva,
            [In] uint flags,
            [In] uint bufferSize,
            [In] IntPtr buffer,
            [In] IntPtr dataSize);

        #endregion

        #region ICLRRuntimeLocator delegate

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetRuntimeBaseDelegate(
            [In] IntPtr self,
            [Out] out ulong address);

        #endregion
    }
}
