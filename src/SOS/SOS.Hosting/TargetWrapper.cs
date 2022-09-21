// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SOS.Hosting
{
    public sealed unsafe class TargetWrapper : COMCallableIUnknown
    {
        // Must be the same as ITarget::OperatingSystem
        enum OperatingSystem
        {
            Unknown         = 0,
            Windows         = 1,
            Linux           = 2,
            OSX             = 3,
        }

        public static readonly Guid IID_ITarget = new Guid("B4640016-6CA0-468E-BA2C-1FFF28DE7B72");

        public ServiceWrapper ServiceWrapper { get; } = new ServiceWrapper();

        public IntPtr ITarget { get; }

        private readonly IServiceProvider _services;
        private readonly ITarget _target;
        private readonly Dictionary<IRuntime, RuntimeWrapper> _wrappers = new Dictionary<IRuntime, RuntimeWrapper>();

        public TargetWrapper(IServiceProvider services)
        {
            _services = services;
            _target = services.GetService<ITarget>() ?? throw new DiagnosticsException("No target");

            VTableBuilder builder = AddInterface(IID_ITarget, validate: false);

            builder.AddMethod(new GetOperatingSystemDelegate(GetOperatingSystem));
            builder.AddMethod(new HostWrapper.GetServiceDelegate(ServiceWrapper.GetService));
            builder.AddMethod(new GetTempDirectoryDelegate(GetTempDirectory));
            builder.AddMethod(new GetRuntimeDelegate(GetRuntime));
            builder.AddMethod(new FlushDelegate(Flush));

            ITarget = builder.Complete();

            AddRef();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("TargetWrapper.Destroy");
            ServiceWrapper.Dispose();
            foreach (RuntimeWrapper wrapper in _wrappers.Values)
            {
                wrapper.Release();
            }
            _wrappers.Clear();
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

        private HResult GetRuntime(
            IntPtr self,
            IntPtr* ppRuntime)
        {
            if (ppRuntime == null) {
                return HResult.E_INVALIDARG;
            }
            IRuntime runtime = _services.GetService<IRuntime>();
            if (runtime == null) {
                return HResult.E_NOINTERFACE;
            }
            if (!_wrappers.TryGetValue(runtime, out RuntimeWrapper wrapper))
            {
                wrapper = new RuntimeWrapper(_services, runtime);
                _wrappers.Add(runtime, wrapper);
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
        private delegate HResult GetRuntimeDelegate(
            [In] IntPtr self,
            [Out] IntPtr* ppRuntime);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void FlushDelegate(
            [In] IntPtr self);

        #endregion
    }
}
