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

        public IntPtr ITarget { get; }

        public static readonly Guid IID_ITarget = new Guid("B4640016-6CA0-468E-BA2C-1FFF28DE7B72");

        private readonly ITarget _target;
        private readonly Dictionary<IRuntime, RuntimeWrapper> _wrappers = new Dictionary<IRuntime, RuntimeWrapper>();

        public TargetWrapper(ITarget target)
        {
            Debug.Assert(target != null);
            _target = target;

            VTableBuilder builder = AddInterface(IID_ITarget, validate: false);

            builder.AddMethod(new GetOperatingSystemDelegate(GetOperatingSystem));
            builder.AddMethod(new GetTempDirectoryDelegate(GetTempDirectory));
            builder.AddMethod(new GetRuntimeDirectoryDelegate(GetRuntimeDirectory));
            builder.AddMethod(new GetRuntimeDelegate(GetRuntime));
            builder.AddMethod(new FlushDelegate(Flush));
            builder.AddMethod(new CloseDelegate(Close));

            ITarget = builder.Complete();

            AddRef();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("TargetWrapper.Destroy");
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

        private string GetRuntimeDirectory(
            IntPtr self)
        {
            var runtimeService = _target.Services.GetService<IRuntimeService>();
            if (runtimeService == null)
            {
                return null;
            }
            return runtimeService.RuntimeModuleDirectory;
        }

        private int GetRuntime(
            IntPtr self,
            IntPtr* ppRuntime)
        {
            if (ppRuntime == null) {
                return HResult.E_INVALIDARG;
            }
            IRuntime runtime = _target.Services.GetService<IRuntimeService>()?.CurrentRuntime;
            if (runtime == null) {
                return HResult.E_NOINTERFACE;
            }
            if (!_wrappers.TryGetValue(runtime, out RuntimeWrapper wrapper))
            {
                wrapper = new RuntimeWrapper(_target, runtime);
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

        private void Close(
            IntPtr self)
        {
            _target.Close();
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
        [return: MarshalAs(UnmanagedType.LPStr)]
        private delegate string GetRuntimeDirectoryDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetRuntimeDelegate(
            [In] IntPtr self,
            [Out] IntPtr* ppRuntime);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void FlushDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void CloseDelegate(
            [In] IntPtr self);

        #endregion
    }
}
