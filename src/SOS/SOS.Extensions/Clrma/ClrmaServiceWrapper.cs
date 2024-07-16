// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Extensions.Clrma
{
    public sealed class ClrmaServiceWrapper : COMCallableIUnknown
    {
        public static readonly Guid IID_ICLRMAService = new("1FCF4C14-60C1-44E6-84ED-20506EF3DC60");

        public const int E_BOUNDS = unchecked((int)0x8000000B);
        public const uint DEBUG_ANY_ID = uint.MaxValue;

        private readonly IServiceProvider _serviceProvider;
        private readonly ServiceWrapper _serviceWrapper;
        private ICrashInfoService _crashInfoService;

        public ClrmaServiceWrapper(ITarget target, IServiceProvider serviceProvider, ServiceWrapper serviceWrapper)
        {
            _serviceProvider = serviceProvider;
            _serviceWrapper = serviceWrapper;

            target.OnFlushEvent.Register(() => _crashInfoService = null);

            VTableBuilder builder = AddInterface(IID_ICLRMAService, validate: false);
            builder.AddMethod(new AssociateClientDelegate(AssociateClient));
            builder.AddMethod(new GetThreadDelegate(GetThread));
            builder.AddMethod(new GetExceptionDelegate(GetException));
            builder.AddMethod(new GetObjectInspectionDelegate(GetObjectInspection));
            builder.Complete();
            // Since this wrapper is only created through a ServiceWrapper factory, no AddRef() is needed.
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("ClrmaServiceWrapper.Destroy");
            _serviceWrapper.RemoveServiceWrapper(ClrmaServiceWrapper.IID_ICLRMAService);
            _crashInfoService = null;
        }

        private int AssociateClient(
            IntPtr self,
            IntPtr punk)
        {
            // If the crash info service doesn't exist, then tell Watson/CLRMA to go on to the next provider
            return CrashInfoService is null ? HResult.E_NOINTERFACE : HResult.S_OK;
        }

        private int GetThread(
            IntPtr self,
            uint osThreadId,
            out IntPtr clrmaClrThread)
        {
            clrmaClrThread = IntPtr.Zero;
            if (CrashInfoService is null)
            {
                return HResult.E_FAIL;
            }
            IThread thread = ThreadService?.GetThreadFromId(osThreadId);
            if (thread is null)
            {
                return HResult.E_INVALIDARG;
            }
            ThreadWrapper threadWrapper = new(CrashInfoService, thread);
            clrmaClrThread = threadWrapper.ICLRMACClrThread;
            return HResult.S_OK;
        }

        private int GetException(
            IntPtr self,
            ulong address,
            out IntPtr clrmaClrException)
        {
            clrmaClrException = IntPtr.Zero;
            IException exception = null;
            try
            {
                exception = CrashInfoService?.GetException(address);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            if (exception is null)
            {
                return HResult.E_INVALIDARG;
            }
            ExceptionWrapper exceptionWrapper = new(exception);
            clrmaClrException = exceptionWrapper.ICLRMACClrException;
            return HResult.S_OK;
        }

        private int GetObjectInspection(
            IntPtr self,
            out IntPtr clrmaObjectInspection)
        {
            clrmaObjectInspection = IntPtr.Zero;
            return HResult.E_NOTIMPL;
        }

        private ICrashInfoService CrashInfoService => _crashInfoService ??= _serviceProvider.GetService<ICrashInfoService>();

        private IThreadService ThreadService => _serviceProvider.GetService<IThreadService>();

        #region ICLRMAService delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AssociateClientDelegate(
            [In] IntPtr self,
            [In] IntPtr punk);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetThreadDelegate(
            [In] IntPtr self,
            [In] uint osThreadId,
            [Out] out IntPtr clrmaClrThread);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetExceptionDelegate(
            [In] IntPtr self,
            [In] ulong address,
            [Out] out IntPtr clrmaClrException);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetObjectInspectionDelegate(
            [In] IntPtr self,
            [Out] out IntPtr objectInspection);

        #endregion
    }
}
