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
    public sealed class ManagedAnalysisWrapper : COMCallableIUnknown
    {
        public static readonly Guid IID_ICLRManagedAnalysis = new("8CA73A16-C017-4c8f-AD51-B758727478CA");

        public const int E_BOUNDS = unchecked((int)0x8000000B);
        public const uint DEBUG_ANY_ID = uint.MaxValue;

        private readonly IServiceProvider _serviceProvider;
        private readonly ServiceWrapper _serviceWrapper;
        private ICrashInfoService _crashInfoService;
        private IDebugClient _debugClient;
        private ThreadWrapper _thread;
        private ExceptionWrapper _exception;

        public ManagedAnalysisWrapper(ITarget target, IServiceProvider serviceProvider, ServiceWrapper serviceWrapper)
        {
            _serviceProvider = serviceProvider;
            _serviceWrapper = serviceWrapper;

            target.OnFlushEvent.Register(Flush);

            VTableBuilder builder = AddInterface(IID_ICLRManagedAnalysis, validate: false);
            builder.AddMethod(new GetProviderNameDelegate(GetProviderName));
            builder.AddMethod(new AssociateClientDelegate(AssociateClient));
            builder.AddMethod(new GetThreadDelegate(GetThread));
            builder.AddMethod(new GetExceptionDelegate(GetException));
            builder.AddMethod(new ObjectInspectionDelegate(ObjectInspection));
            builder.Complete();
            // Since this wrapper is only created through a ServiceWrapper factory, no AddRef() is needed.
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("ManagedAnalysisWrapper.Destroy");
            _serviceWrapper.RemoveServiceWrapper(ManagedAnalysisWrapper.IID_ICLRManagedAnalysis);
            Flush();
        }

        private void Flush()
        {
            _crashInfoService = null;
            FlushDebugClient();
            FlushThread();
            FlushException();
        }

        private void FlushDebugClient()
        {
            if (_debugClient != null)
            {
                int count = Marshal.ReleaseComObject(_debugClient);
                Debug.Assert(count >= 0);
                _debugClient = null;
            }
        }

        private void FlushThread()
        {
            _thread?.ReleaseWithCheck();
            _thread = null;
        }

        private void FlushException()
        {
            _exception?.ReleaseWithCheck();
            _exception = null;
        }

        private int GetProviderName(
            IntPtr self,
            out string provider)
        {
            provider = "SOSCLRMA";
            return HResult.S_OK;
        }

        private int AssociateClient(
            IntPtr self,
            IntPtr punk)
        {
            // If the crash info service doesn't exist, then tell Watson/CLRMA to go on to the next provider
            if (CrashInfoService is null)
            {
                return HResult.E_NOINTERFACE;
            }
            FlushDebugClient();
            _debugClient = Marshal.GetObjectForIUnknown(punk) as IDebugClient;
            if (_debugClient == null)
            {
                return HResult.E_NOINTERFACE;
            }
            // We don't currently need the IDebugClient instance passed this this function.
            return HResult.S_OK;
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
            // osThreadId == 0 is current thread and -1 is "last event thread". The only thread
            // information we have currently is the crashing thread id so always return it.
            if (osThreadId == 0)
            {
                HResult hr = ((IDebugSystemObjects)_debugClient).GetCurrentThreadSystemId(out osThreadId);
                if (!hr.IsOK)
                {
                    return hr;
                }
            }
            else if (osThreadId == uint.MaxValue)
            {
                HResult hr = ((IDebugControl)_debugClient).GetLastEventInformation(
                    out DEBUG_EVENT _,
                    out uint _,
                    out uint threadIndex,
                    IntPtr.Zero,
                    0,
                    out uint _,
                    null,
                    0,
                    out uint _);

                if (!hr.IsOK)
                {
                    return hr;
                }
                if (threadIndex == DEBUG_ANY_ID)
                {
                    return HResult.E_INVALIDARG;
                }
                uint[] ids = new uint[1];
                uint[] sysIds = new uint[1];
                hr = ((IDebugSystemObjects)_debugClient).GetThreadIdsByIndex(threadIndex, 1, ids, sysIds);
                if (!hr.IsOK)
                {
                    return hr;
                }
                osThreadId = sysIds[0];
            }
            if (_thread is null || _thread.ThreadId != osThreadId)
            {
                IThread thread = ThreadService?.GetThreadFromId(osThreadId);
                if (thread is null)
                {
                    return HResult.E_INVALIDARG;
                }
                FlushThread();
                _thread = new ThreadWrapper(CrashInfoService, thread);
            }
            _thread.AddRef();
            clrmaClrThread = _thread.ICLRMACClrThread;
            return HResult.S_OK;
        }

        private int GetException(
            IntPtr self,
            ulong address,
            out IntPtr clrmaClrException)
        {
            clrmaClrException = IntPtr.Zero;
            if (_exception is null || _exception.Exception.Address != address)
            {
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
                FlushException();
                _exception = new ExceptionWrapper(exception);
            }
            _exception.AddRef();
            clrmaClrException = _exception.ICLRMACClrException;
            return HResult.S_OK;
        }

        private int ObjectInspection(
            IntPtr self,
            out IntPtr clrmaObjectInspection)
        {
            clrmaObjectInspection = IntPtr.Zero;
            return HResult.E_NOTIMPL;
        }

        private ICrashInfoService CrashInfoService => _crashInfoService ??= _serviceProvider.GetService<ICrashInfoService>();

        private IThreadService ThreadService => _serviceProvider.GetService<IThreadService>();

        #region ICLRManagedAnalysis delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetProviderNameDelegate(
            [In] IntPtr self,
            [Out, MarshalAs(UnmanagedType.BStr)] out string provider);

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
        private delegate int ObjectInspectionDelegate(
            [In] IntPtr self,
            [Out] out IntPtr objectInspection);

        #endregion
    }
}
