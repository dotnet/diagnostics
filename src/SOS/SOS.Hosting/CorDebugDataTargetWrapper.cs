// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SOS.Hosting
{
    public sealed unsafe class CorDebugDataTargetWrapper : COMCallableIUnknown
    {
        private static readonly Guid IID_ICorDebugDataTarget = new Guid("FE06DC28-49FB-4636-A4A3-E80DB4AE116C");
        private static readonly Guid IID_ICorDebugDataTarget4 = new Guid("E799DC06-E099-4713-BDD9-906D3CC02CF2");
        private static readonly Guid IID_ICorDebugMutableDataTarget = new Guid("A1B8A756-3CB6-4CCB-979F-3DF999673A59");
        private static readonly Guid IID_ICorDebugMetaDataLocator = new Guid("7cef8ba9-2ef7-42bf-973f-4171474f87d9");

        private readonly ITarget _target;
        private readonly ISymbolService _symbolService;
        private readonly IMemoryService _memoryService;
        private readonly IThreadService _threadService;
        private readonly IThreadUnwindService _threadUnwindService;
        private readonly ulong _ignoreAddressBitsMask;

        public IntPtr ICorDebugDataTarget { get; }

        public CorDebugDataTargetWrapper(IServiceProvider services)
        {
            Debug.Assert(services != null);
            _target = services.GetService<ITarget>();
            _symbolService = services.GetService<ISymbolService>();
            _memoryService = services.GetService<IMemoryService>();
            _threadService = services.GetService<IThreadService>();
            _threadUnwindService = services.GetService<IThreadUnwindService>();
            _ignoreAddressBitsMask = _memoryService.SignExtensionMask();

            VTableBuilder builder = AddInterface(IID_ICorDebugDataTarget, validate: false);
            builder.AddMethod(new GetPlatformDelegate(GetPlatform));
            builder.AddMethod(new ReadVirtualDelegate(ReadVirtual));
            builder.AddMethod(new GetThreadContextDelegate(GetThreadContext));
            ICorDebugDataTarget = builder.Complete();

            builder = AddInterface(IID_ICorDebugDataTarget4, validate: false);
            builder.AddMethod(new VirtualUnwindDelegate(VirtualUnwind));
            builder.Complete();

            builder = AddInterface(IID_ICorDebugMutableDataTarget, validate: false);
            builder.AddMethod(new WriteVirtualDelegate(WriteVirtual));
            builder.AddMethod(new SetThreadContextDelegate((self, threadId, contextSize, context) => HResult.E_NOTIMPL));
            builder.AddMethod(new ContinueStatusChangeDelegate((self, continueStatus) => HResult.E_NOTIMPL));
            builder.Complete();

            builder = AddInterface(IID_ICorDebugMetaDataLocator, validate: false);
            builder.AddMethod(new GetMetaDataDelegate(GetMetaData));
            builder.Complete();

            AddRef();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("CorDebugDataTargetWrapper.Destroy");
        }

        #region ICorDebugDataTarget

        private int GetPlatform(
            IntPtr self,
            out CorDebugPlatform platform)
        {
            platform = CorDebugPlatform.CORDB_PLATFORM_WINDOWS_AMD64;
            if (_target.OperatingSystem == OSPlatform.Windows)
            {
                switch (_target.Architecture)
                {
                    case Architecture.X64:
                        platform = CorDebugPlatform.CORDB_PLATFORM_WINDOWS_AMD64;
                        break;
                    case Architecture.X86:
                        platform = CorDebugPlatform.CORDB_PLATFORM_WINDOWS_X86;
                        break;
                    case Architecture.Arm:
                        platform = CorDebugPlatform.CORDB_PLATFORM_WINDOWS_ARM;
                        break;
                    case Architecture.Arm64:
                        platform = CorDebugPlatform.CORDB_PLATFORM_WINDOWS_ARM64;
                        break;
                    default:
                        return HResult.E_FAIL;
                }
            } 
            else if (_target.OperatingSystem == OSPlatform.Linux || _target.OperatingSystem == OSPlatform.OSX)
            {
                switch (_target.Architecture)
                {
                    case Architecture.X64:
                        platform = CorDebugPlatform.CORDB_PLATFORM_POSIX_AMD64;
                        break;
                    case Architecture.X86:
                        platform = CorDebugPlatform.CORDB_PLATFORM_POSIX_X86;
                        break;
                    case Architecture.Arm:
                        platform = CorDebugPlatform.CORDB_PLATFORM_POSIX_ARM;
                        break;
                    case Architecture.Arm64:
                        platform = CorDebugPlatform.CORDB_PLATFORM_POSIX_ARM64;
                        break;
                    default:
                        return HResult.E_FAIL;
                }
            }
            else
            {
                return HResult.E_FAIL;
            }
            return HResult.S_OK;
        }

        private unsafe int ReadVirtual(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            uint bytesRequested,
            uint* pbytesRead)
        {
            address &= _ignoreAddressBitsMask;
            if (!_memoryService.ReadMemory(address, buffer, unchecked((int)bytesRequested), out int bytesRead))
            {
                Trace.TraceError("CorDebugDataTargetWrappter.ReadVirtual FAILED address {0:X16} size {1:X8}", address, bytesRequested);
                return HResult.E_FAIL;
            }
            SOSHost.Write(pbytesRead, (uint)bytesRead);
            return HResult.S_OK;
        }

        private int GetThreadContext(
            IntPtr self,
            uint threadId,
            uint contextFlags,
            uint contextSize,
            IntPtr context)
        {
            byte[] registerContext;
            try
            {
                registerContext = _threadService.GetThreadFromId(threadId).GetThreadContext();
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

        #endregion

        #region ICorDebugDataTarget4

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

        #region ICorDebugMutableDataTarget

        private unsafe int WriteVirtual(
            IntPtr self,
            ulong address,
            IntPtr buffer,
            uint bytesRequested)
        {
            address &= _ignoreAddressBitsMask;
            if (!_memoryService.WriteMemory(address, new Span<byte>(buffer.ToPointer(), unchecked((int)bytesRequested)), out _))
            {
                return HResult.E_FAIL;
            }
            return HResult.S_OK;
        }

        #endregion

        #region ICorDebugMetaDataLocator

        private int GetMetaData(
            IntPtr self,
            string imagePath,
            uint imageTimestamp,
            uint imageSize,
            uint pathBufferSize,
            IntPtr pPathBufferSize,
            IntPtr pPathBuffer)
        {
            return _symbolService.GetICorDebugMetadataLocator(imagePath, imageTimestamp, imageSize, pathBufferSize, pPathBufferSize, pPathBuffer);
        }

        #endregion

        #region ICorDebugDataTarget delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetPlatformDelegate(
            [In] IntPtr self,
            [Out] out CorDebugPlatform platform);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadVirtualDelegate(
            [In] IntPtr self,
            [In] ulong address,
            [Out] IntPtr buffer,
            [In] uint bytesRequested,
            [Out] uint* pbytesRead);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetThreadContextDelegate(
            [In] IntPtr self,
            [In] uint threadId,
            [In] uint contextFlags,
            [In] uint contextSize,
            [Out] IntPtr context);

        #endregion

        #region ICorDebugDataTarget4 delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int VirtualUnwindDelegate(
            [In] IntPtr self,
            [In] uint threadId,
            [In] uint contextSize,
            [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] context);

        #endregion

        #region ICorDebugMutableDataTarget delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteVirtualDelegate(
            [In] IntPtr self,
            [In] ulong address,
            [In] IntPtr buffer,
            [In] uint bytesRequested);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetThreadContextDelegate(
            [In] IntPtr self,
            [In] uint threadId,
            [In] uint contextSize,
            [In] IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ContinueStatusChangeDelegate(
            [In] IntPtr self,
            [In] uint continueStatus);

        #endregion

        #region ICorDebugMetaDataLocator delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetMetaDataDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPWStr)] string imagePath,
            [In] uint imageTimestamp,
            [In] uint imageSize,
            [In] uint pathBufferSize,
            [Out] IntPtr pPathBufferSize,
            [Out] IntPtr pPathBuffer);

        #endregion
    }
}
