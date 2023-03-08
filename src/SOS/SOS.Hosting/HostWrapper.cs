﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS.Hosting
{
    public sealed class HostWrapper : COMCallableIUnknown
    {
        private static readonly Guid IID_IHost = new("E0CD8534-A88B-40D7-91BA-1B4C925761E9");

        private readonly IHost _host;

        public ServiceWrapper ServiceWrapper { get; } = new ServiceWrapper();

        public IntPtr IHost { get; }

        public HostWrapper(IHost host)
        {
            _host = host;

            VTableBuilder builder = AddInterface(IID_IHost, validate: false);
            builder.AddMethod(new GetHostTypeDelegate(GetHostType));
            builder.AddMethod(new GetServiceDelegate(ServiceWrapper.GetService));
            builder.AddMethod(new GetCurrentTargetDelegate(GetCurrentTarget));
            IHost = builder.Complete();

            AddRef();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("HostWrapper.Destroy");
            ServiceWrapper.Dispose();
        }

        #region IHost

        /// <summary>
        /// Returns the host type
        /// </summary>
        private HostType GetHostType(IntPtr self) => _host.HostType;

        /// <summary>
        /// Returns the current target wrapper or null
        /// </summary>
        /// <param name="targetWrapper">target wrapper address returned</param>
        /// <returns>S_OK</returns>
        private int GetCurrentTarget(IntPtr self, out IntPtr targetWrapper)
        {
            IContextService contextService = _host.Services.GetService<IContextService>();
            ITarget target = contextService.GetCurrentTarget();
            TargetWrapper wrapper = target?.Services.GetService<TargetWrapper>();
            if (wrapper == null)
            {
                targetWrapper = IntPtr.Zero;
                return HResult.E_NOINTERFACE;
            }
            wrapper.AddRef();
            targetWrapper = wrapper.ITarget;
            return HResult.S_OK;
        }

        #endregion

        #region IHost delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HostType GetHostTypeDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int GetServiceDelegate(
            [In] IntPtr self,
            [In] in Guid guid,
            [Out] out IntPtr ptr);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentTargetDelegate(
            [In] IntPtr self,
            [Out] out IntPtr target);

        #endregion
    }
}
