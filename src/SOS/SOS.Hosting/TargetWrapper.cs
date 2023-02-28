// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS.Hosting
{
    [ServiceExport(Scope = ServiceScope.Target)]
    public sealed unsafe class TargetWrapper : COMCallableIUnknown, IDisposable
    {
        // Must be the same as ITarget::OperatingSystem
        private enum OperatingSystem
        {
            Unknown         = 0,
            Windows         = 1,
            Linux           = 2,
            OSX             = 3,
        }

        public static readonly Guid IID_ITarget = new Guid("B4640016-6CA0-468E-BA2C-1FFF28DE7B72");

        public ServiceWrapper ServiceWrapper { get; } = new ServiceWrapper();

        public IntPtr ITarget { get; }

        private readonly ITarget _target;
        private readonly IContextService _contextService;

        public TargetWrapper(ITarget target, IContextService contextService, ISymbolService symbolService, IMemoryService memoryService)
        {
            Debug.Assert(target != null);
            Debug.Assert(contextService != null);
            Debug.Assert(symbolService != null);
            _target = target;
            _contextService = contextService;

            ServiceWrapper.AddServiceWrapper(SymbolServiceWrapper.IID_ISymbolService, () => new SymbolServiceWrapper(symbolService, memoryService));

            VTableBuilder builder = AddInterface(IID_ITarget, validate: false);

            builder.AddMethod(new GetOperatingSystemDelegate(GetOperatingSystem));
            builder.AddMethod(new HostWrapper.GetServiceDelegate(ServiceWrapper.GetService));
            builder.AddMethod(new GetTempDirectoryDelegate(GetTempDirectory));
            builder.AddMethod(new GetRuntimeDelegate(GetRuntime));
            builder.AddMethod(new FlushDelegate(Flush));

            ITarget = builder.Complete();

            AddRef();
        }

        void IDisposable.Dispose()
        {
            Trace.TraceInformation("TargetWrapper.Dispose");
            this.ReleaseWithCheck();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("TargetWrapper.Destroy");
            ServiceWrapper.RemoveServiceWrapper(SymbolServiceWrapper.IID_ISymbolService);
            ServiceWrapper.Dispose();
        }

        private OperatingSystem GetOperatingSystem(
            IntPtr self)
        {
            if (_target.OperatingSystem == OSPlatform.Windows) {
                return OperatingSystem.Windows;
            } 
            else if (_target.OperatingSystem == OSPlatform.Linux) {
                return OperatingSystem.Linux;
            }
            else if (_target.OperatingSystem == OSPlatform.OSX) {
                return OperatingSystem.OSX;
            }
            return OperatingSystem.Unknown;
        }

        private string GetTempDirectory(
            IntPtr self)
        {
            return _target.GetTempDirectory();
        }

        private int GetRuntime(
            IntPtr self,
            IntPtr* ppRuntime)
        {
            if (ppRuntime == null) {
                return HResult.E_INVALIDARG;
            }
            IRuntime runtime = _contextService.GetCurrentRuntime();
            if (runtime is null) {
                return HResult.E_NOINTERFACE;
            }
            RuntimeWrapper wrapper = runtime.Services.GetService<RuntimeWrapper>();
            if (wrapper is null) {
                return HResult.E_NOINTERFACE;
            }
            *ppRuntime = wrapper.IRuntime;
            return HResult.S_OK;
        }

        private void Flush(
            IntPtr self)
        {
            _target.Flush();
        }

        #region ITarget delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate OperatingSystem GetOperatingSystemDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.LPStr)]
        private delegate string GetTempDirectoryDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetRuntimeDelegate(
            [In] IntPtr self,
            [Out] IntPtr* ppRuntime);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void FlushDelegate(
            [In] IntPtr self);

        #endregion
    }
}
