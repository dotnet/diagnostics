// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS.Extensions.Clrma
{
    public sealed class ExceptionWrapper : COMCallableIUnknown
    {
        public static readonly Guid IID_ICLRMAClrException = new("7C165652-D539-472e-A6CF-F657FFF31751");

        public IException Exception { get; }

        public IntPtr ICLRMACClrException { get; }

        private ExceptionWrapper[] _innerExceptions;

        public ExceptionWrapper(IException exception)
        {
            Debug.Assert(exception != null);
            Exception = exception;

            VTableBuilder builder = AddInterface(IID_ICLRMAClrException, validate: false);
            builder.AddMethod(new DebuggerCommandDelegate(DebuggerCommand));
            builder.AddMethod(new AddressDelegate(GetAddress));
            builder.AddMethod(new HResultDelegate(GetHResult));
            builder.AddMethod(new TypeDelegate(GetType));
            builder.AddMethod(new MessageDelegate(GetMessage));
            builder.AddMethod(new FrameCountDelegate(FrameCount));
            builder.AddMethod(new FrameDelegate(Frame));
            builder.AddMethod(new InnerExceptionCountDelegate(InnerExceptionCount));
            builder.AddMethod(new InnerExceptionDelegate(InnerException));
            ICLRMACClrException = builder.Complete();

            AddRef();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("ExceptionWrapper.Destroy");
        }

        private int DebuggerCommand(
            IntPtr self,
            out string command)
        {
            command = null;
            return HResult.S_FALSE;
        }

        private int GetAddress(
            IntPtr self,
            out ulong address)
        {
            address = Exception.Address;
            return HResult.S_OK;
        }

        private int GetHResult(
            IntPtr self,
            out uint hresult)
        {
            hresult = Exception.HResult;
            return HResult.S_OK;
        }

        private int GetType(
            IntPtr self,
            out string type)
        {
            type = Exception.Type;
            return HResult.S_OK;
        }

        private int GetMessage(
            IntPtr self,
            out string message)
        {
            message = Exception.Message;
            return HResult.S_OK;
        }

        private int FrameCount(
            IntPtr self,
            out int count)
        {
            count = Exception.Stack.FrameCount;
            return count > 0 ? HResult.S_OK : HResult.S_FALSE;
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
            IStackFrame frame;
            try
            {
                frame = Exception.Stack.GetStackFrame(nFrame);
            }
            catch (ArgumentOutOfRangeException)
            {
                return ManagedAnalysisWrapper.E_BOUNDS;
            }
            ip = frame.InstructionPointer;
            sp = frame.StackPointer;
            frame.GetMethodName(out moduleName, out functionName, out displacement);
            moduleName ??= $"module_{frame.ModuleBase:X16}";
            functionName ??= $"function_{frame.InstructionPointer:X16}";
            return HResult.S_OK;
        }

        private int InnerExceptionCount(
            IntPtr self,
            out ushort count)
        {
            count = (ushort)InnerExceptions.Length;
            return count > 0 ? HResult.S_OK : HResult.S_FALSE;
        }

        private int InnerException(
            IntPtr self,
            ushort index,
            out IntPtr clrmaClrException)
        {
            clrmaClrException = IntPtr.Zero;
            if (index >= InnerExceptions.Length)
            {
                return ManagedAnalysisWrapper.E_BOUNDS;
            }
            ExceptionWrapper exception = InnerExceptions[index];
            exception.AddRef();
            clrmaClrException = exception.ICLRMACClrException;
            return HResult.S_OK;
        }

        private ExceptionWrapper[] InnerExceptions
        {
            get { return _innerExceptions ??= Exception.InnerExceptions.Select((exception) => new ExceptionWrapper(exception)).ToArray(); }
        }

        #region ICLRMAClrException delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int DebuggerCommandDelegate(
            [In] IntPtr self,
            [Out, MarshalAs(UnmanagedType.BStr)] out string command);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AddressDelegate(
            [In] IntPtr self,
            [Out] out ulong address);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int HResultDelegate(
            [In] IntPtr self,
            [Out] out uint hresult);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int TypeDelegate(
            [In] IntPtr self,
            [Out, MarshalAs(UnmanagedType.BStr)] out string type);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int MessageDelegate(
            [In] IntPtr self,
            [Out, MarshalAs(UnmanagedType.BStr)] out string message);

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
        private delegate int InnerExceptionCountDelegate(
            [In] IntPtr self,
            [Out] out ushort count);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int InnerExceptionDelegate(
            [In] IntPtr self,
            [In] ushort index,
            [Out] out IntPtr clrmaClrException);

        #endregion
    }
}
