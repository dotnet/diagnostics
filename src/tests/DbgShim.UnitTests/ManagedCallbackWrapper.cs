// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics
{
    public sealed class ManagedCallbackWrapper : COMCallableIUnknown 
    {
        private static readonly Guid IID_ICorDebugManagedCallback = new Guid("3D6F5F60-7538-11D3-8D5B-00104B35E7EF");
        private static readonly Guid IID_ICorDebugManagedCallback2 = new Guid("250E5EEA-DB5C-4C76-B6F3-8C46F12E3203");

        private readonly DebuggeeInfo _startInfo;

        public IntPtr ICorDebugManagedCallback { get; }

        public ManagedCallbackWrapper(DebuggeeInfo startInfo)
        {
            _startInfo = startInfo;

            VTableBuilder builder = AddInterface(IID_ICorDebugManagedCallback, validate: false);
            builder.AddMethod(new BreakpointDelegate((self, pAppDomain, pThread, pBreakpoint) => HResult.E_NOTIMPL));
            builder.AddMethod(new StepCompleteDelegate((self, pAppDomain, pThread, pStepper, reason) => HResult.E_NOTIMPL));
            builder.AddMethod(new BreakDelegate((self, pAppDomain, pThread) => WriteLine("Break")));
            builder.AddMethod(new ExceptionDelegate((self, pAppDomain, pThread, unhandled) => WriteLine("Exception")));
            builder.AddMethod(new EvalCompleteDelegate((self, pAppDomain, pThread, pEval) => HResult.E_NOTIMPL));
            builder.AddMethod(new EvalExceptionDelegate((self, pAppDomain, pThread, pEval) => HResult.E_NOTIMPL));
            builder.AddMethod(new CreateProcessDelegate((self, pProcess) => CreateProcess(pProcess)));
            builder.AddMethod(new ExitProcessDelegate((self, pProcess) => WriteLine("ExitProcess")));
            builder.AddMethod(new CreateThreadDelegate((self, pAppDomain, pThread) => HResult.E_NOTIMPL));
            builder.AddMethod(new ExitThreadDelegate((self, pAppDomain, pThread) => HResult.E_NOTIMPL));
            builder.AddMethod(new LoadModuleDelegate((self, pAppDomain, pModule) => HResult.E_NOTIMPL));
            builder.AddMethod(new UnloadModuleDelegate((self, pAppDomain, pModule) => HResult.E_NOTIMPL));
            builder.AddMethod(new LoadClassDelegate((self, pAppDomain, c) => HResult.E_NOTIMPL));
            builder.AddMethod(new UnloadClassDelegate((self, pAppDomain, c) => HResult.E_NOTIMPL));
            builder.AddMethod(new DebuggerErrorDelegate((self, pProcess, errorHR, errorCode) => WriteLine($"DebuggerError {errorHR} {errorCode:X8}")));
            builder.AddMethod(new LogMessageDelegate((self, pAppDomain, pThread, lLevel, pLogSwitchName, pMessage) => HResult.E_NOTIMPL));
            builder.AddMethod(new LogSwitchDelegate((self, pAppDomain, pThread, lLevel, ulReason, pLogSwitchName, pParentName) => HResult.E_NOTIMPL));
            builder.AddMethod(new CreateAppDomainDelegate((self, pProcess, pAppDomain) => HResult.E_NOTIMPL));
            builder.AddMethod(new ExitAppDomainDelegate((self, pProcess, pAppDomain) => HResult.E_NOTIMPL));
            builder.AddMethod(new LoadAssemblyDelegate((self, pAppDomain, pAssembly) => HResult.E_NOTIMPL));
            builder.AddMethod(new UnloadAssemblyDelegate((self, pAppDomain, pAssembly) => HResult.E_NOTIMPL));
            builder.AddMethod(new ControlCTrapDelegate((self, pProcess) => HResult.E_NOTIMPL));
            builder.AddMethod(new NameChangeDelegate((self, pAppDomain, pThread) => HResult.E_NOTIMPL));
            builder.AddMethod(new UpdateModuleSymbolsDelegate((self, pAppDomain, pModule, pSymbolStream) => HResult.E_NOTIMPL));
            builder.AddMethod(new EditAndContinueRemapDelegate((self, pAppDomain, pThread, pFunction, fAccurate) => HResult.E_NOTIMPL));
            builder.AddMethod(new BreakpointSetErrorDelegate((self, pAppDomain, pThread, pBreakpoint, dwError) => HResult.E_NOTIMPL));
            ICorDebugManagedCallback = builder.Complete();

            builder = AddInterface(IID_ICorDebugManagedCallback2, validate: false);
            builder.AddMethod(new FunctionRemapOpportunityDelegate((self, pAppDomain, pThread, pOldFunction, pNewFunction, oldILOffset) => HResult.E_NOTIMPL));
            builder.AddMethod(new CreateConnectionDelegate((IntPtr self, IntPtr pProcess, uint dwConnectionId, ref ushort pConnName) => HResult.E_NOTIMPL));
            builder.AddMethod(new ChangeConnectionDelegate((self, pProcess, dwConnectionId) => HResult.E_NOTIMPL));
            builder.AddMethod(new DestroyConnectionDelegate((self, pProcess, dwConnectionId) => HResult.E_NOTIMPL));
            builder.AddMethod(new ExceptionDelegate2((self, pAppDomain, pThread, pFrame, nOffset, dwEventType, dwFlags) => HResult.E_NOTIMPL));
            builder.AddMethod(new ExceptionUnwindDelegate((self, pAppDomain, pThread, dwEventType, dwFlags) => HResult.E_NOTIMPL));
            builder.AddMethod(new FunctionRemapCompleteDelegate((self, pAppDomain, pThread, pFunction) => HResult.E_NOTIMPL));
            builder.AddMethod(new MDANotificationDelegate((self, pController, pThread, pMDA) => HResult.E_NOTIMPL));
            builder.Complete();

            AddRef();
        }

        private HResult CreateProcess(IntPtr pController)
        {
            Trace.TraceInformation("ManagedCallbackWrapper.CreateProcess");
            ICorDebugController process = ICorDebugController.Create(pController);
            _startInfo.SetCreateProcessResult(process.Continue(isOutOfBand: false));
            process.Release();
            return HResult.S_OK;
        }

        private HResult WriteLine(string message)
        {
            Trace.TraceInformation("ManagedCallbackWrapper." + message);
            return HResult.E_NOTIMPL;
        }

        #region ICorDebugManagedCallback delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult BreakpointDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] IntPtr pBreakpoint);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult StepCompleteDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] IntPtr pStepper, [In] int reason);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult BreakDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult ExceptionDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] int unhandled);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult EvalCompleteDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] IntPtr pEval);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult EvalExceptionDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] IntPtr pEval);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult CreateProcessDelegate([In] IntPtr self, [In] IntPtr pProcess);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult ExitProcessDelegate([In] IntPtr self, [In] IntPtr pProcess);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult CreateThreadDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr thread);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult ExitThreadDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr thread);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult LoadModuleDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pModule);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult UnloadModuleDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pModule);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult LoadClassDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr c);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult UnloadClassDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr c);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult DebuggerErrorDelegate([In] IntPtr self, [In] IntPtr pProcess, [In] HResult errorHR, [In] uint errorCode);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult LogMessageDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] int lLevel, [In, MarshalAs(UnmanagedType.LPWStr)] string pLogSwitchName, [In, MarshalAs(UnmanagedType.LPWStr)] string pMessage);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult LogSwitchDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] int lLevel, [In] uint ulReason, [In, MarshalAs(UnmanagedType.LPWStr)] string pLogSwitchName, [In, MarshalAs(UnmanagedType.LPWStr)] string pParentName);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult CreateAppDomainDelegate([In] IntPtr self, [In] IntPtr pProcess, [In] IntPtr pAppDomain);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult ExitAppDomainDelegate([In] IntPtr self, [In] IntPtr pProcess, [In] IntPtr pAppDomain);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult LoadAssemblyDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pAssembly);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult UnloadAssemblyDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pAssembly);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult ControlCTrapDelegate([In] IntPtr self, [In] IntPtr pProcess);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult NameChangeDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult UpdateModuleSymbolsDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pModule, [In] IntPtr pSymbolStream);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult EditAndContinueRemapDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] IntPtr pFunction, [In] int fAccurate);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult BreakpointSetErrorDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] IntPtr pBreakpoint, [In] uint dwError);

        #endregion

        #region ICorDebugManagedCallback2 delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult FunctionRemapOpportunityDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] IntPtr pOldFunction, [In] IntPtr pNewFunction, [In] uint oldILOffset);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult CreateConnectionDelegate([In] IntPtr self, [In] IntPtr pProcess, [In] uint dwConnectionId, [In] ref ushort pConnName);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult ChangeConnectionDelegate([In] IntPtr self, [In] IntPtr pProcess, [In] uint dwConnectionId);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult DestroyConnectionDelegate([In] IntPtr self, [In] IntPtr pProcess, [In] uint dwConnectionId);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult ExceptionDelegate2([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] IntPtr pFrame, [In] uint nOffset, [In] int dwEventType, [In] uint dwFlags);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult ExceptionUnwindDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] int dwEventType, [In] uint dwFlags);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult FunctionRemapCompleteDelegate([In] IntPtr self, [In] IntPtr pAppDomain, [In] IntPtr pThread, [In] IntPtr pFunction);
        
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult MDANotificationDelegate([In] IntPtr self, [In] IntPtr pController, [In] IntPtr pThread, [In] IntPtr pMDA);

        #endregion
    }
}
