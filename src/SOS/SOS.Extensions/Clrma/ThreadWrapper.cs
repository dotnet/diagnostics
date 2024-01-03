// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting;

namespace SOS.Extensions.Clrma
{
    public sealed class ThreadWrapper : COMCallableIUnknown
    {
        public static readonly Guid IID_ICLRMAClrThread = new("9849CFC9-0868-406e-9059-6B04E9ADBBB8");

        public uint ThreadId => _thread.ThreadId;

        public IntPtr ICLRMACClrThread { get; }

        private readonly ICrashInfoService _crashInfoService;
        private readonly IThread _thread;
        private ExceptionWrapper _currentException;
        private ExceptionWrapper[] _nestedExceptions;

        public ThreadWrapper(ICrashInfoService crashInfoService, IThread thread)
        {
            Debug.Assert(crashInfoService != null);
            _crashInfoService = crashInfoService;
            _thread = thread;

            VTableBuilder builder = AddInterface(IID_ICLRMAClrThread, validate: false);
            builder.AddMethod(new DebuggerCommandDelegate(DebuggerCommand));
            builder.AddMethod(new OSThreadIdDelegate(OSThreadId));
            builder.AddMethod(new FrameCountDelegate(FrameCount));
            builder.AddMethod(new FrameDelegate(Frame));
            builder.AddMethod(new CurrentExceptionDelegate(CurrentException));
            builder.AddMethod(new NestedExceptionCountDelegate(NestedExceptionCount));
            builder.AddMethod(new NestedExceptionDelegate(NestedException));
            ICLRMACClrThread = builder.Complete();

            AddRef();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("ThreadWrapper.Destroy");
            _currentException?.ReleaseWithCheck();
            _currentException = null;
        }

        private int DebuggerCommand(
            IntPtr self,
            out string command)
        {
            command = null;
            return HResult.S_FALSE;
        }

        private int OSThreadId(
            IntPtr self,
            out uint osThreadId)
        {
            osThreadId = ThreadId;
            return osThreadId > 0 ? HResult.S_OK : HResult.S_FALSE;
        }

        private int FrameCount(
            IntPtr self,
            out int count)
        {
            count = 0;
            return HResult.E_NOTIMPL;
        }

        private int Frame(
            IntPtr self,
            int nFrame,
            out ulong ip,
            out ulong sp,
            out string moduleName,
            out string functionName,
            out ulong displacement)
        {
            ip = 0;
            sp = 0;
            moduleName = null;
            functionName = null;
            displacement = 0;
            return HResult.E_NOTIMPL;
        }

        private int CurrentException(
            IntPtr self,
            out IntPtr clrmaClrException)
        {
            clrmaClrException = IntPtr.Zero;
            if (_currentException is null)
            {
                IException exception = null;
                try
                {
                    exception = _crashInfoService.GetThreadException(ThreadId);
                }
                catch (ArgumentOutOfRangeException)
                {
                }
                if (exception is null)
                {
                    return HResult.S_FALSE;
                }
                _currentException ??= new ExceptionWrapper(exception);
            }
            _currentException.AddRef();
            clrmaClrException = _currentException.ICLRMACClrException;
            return HResult.S_OK;
        }

        private int NestedExceptionCount(
            IntPtr self,
            out ushort count)
        {
            count = (ushort)NestedExceptions.Length;
            return count > 0 ? HResult.S_OK : HResult.S_FALSE;
        }

        private int NestedException(
            IntPtr self,
            ushort index,
            out IntPtr clrmaClrException)
        {
            clrmaClrException = IntPtr.Zero;
            if (index >= NestedExceptions.Length)
            {
                return ManagedAnalysisWrapper.E_BOUNDS;
            }
            ExceptionWrapper exception = NestedExceptions[index];
            exception.AddRef();
            clrmaClrException = exception.ICLRMACClrException;
            return HResult.S_OK;
        }

        private ExceptionWrapper[] NestedExceptions
        {
            get
            {
                if (_nestedExceptions is null)
                {
                    try
                    {
                        _nestedExceptions = _crashInfoService.GetNestedExceptions(ThreadId).Select((exception) => new ExceptionWrapper(exception)).ToArray();
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        _nestedExceptions = Array.Empty<ExceptionWrapper>();
                    }
                }
                return _nestedExceptions;
            }
        }

        #region ICLRMAClrThread delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int DebuggerCommandDelegate(
            [In] IntPtr self,
            [Out, MarshalAs(UnmanagedType.BStr)] out string command);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OSThreadIdDelegate(
            [In] IntPtr self,
            [Out] out uint osThreadId);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int FrameCountDelegate(
            [In] IntPtr self,
            [Out] out int count);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int FrameDelegate(
            [In] IntPtr self,
            [In] int nFrame,
            [Out] out ulong ip,
            [Out] out ulong sp,
            [Out, MarshalAs(UnmanagedType.BStr)] out string moduleName,
            [Out, MarshalAs(UnmanagedType.BStr)] out string functionName,
            [Out] out ulong displacement);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CurrentExceptionDelegate(
            [In] IntPtr self,
            [Out] out IntPtr clrmaClrException);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int NestedExceptionCountDelegate(
            [In] IntPtr self,
            [Out] out ushort count);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int NestedExceptionDelegate(
            [In] IntPtr self,
            [In] ushort index,
            [Out] out IntPtr clrmaClrException);

        #endregion
    }
}
