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
    public sealed class HostWrapper : COMCallableIUnknown 
    {
        private static readonly Guid IID_IHost = new Guid("E0CD8534-A88B-40D7-91BA-1B4C925761E9");

        private readonly IHost _host;
        private readonly Dictionary<Guid, Func<COMCallableIUnknown>> _factories = new Dictionary<Guid, Func<COMCallableIUnknown>>();
        private readonly Dictionary<Guid, COMCallableIUnknown> _wrappers = new Dictionary<Guid, COMCallableIUnknown>();

        private TargetWrapper _targetWrapper;

        public IntPtr IHost { get; }

        public HostWrapper(IHost host)
        {
            _host = host;

            VTableBuilder builder = AddInterface(IID_IHost, validate: false);
            builder.AddMethod(new GetHostTypeDelegate(GetHostType));
            builder.AddMethod(new GetServiceDelegate(GetService));
            builder.AddMethod(new GetCurrentTargetDelegate(GetCurrentTarget));
            IHost = builder.Complete();

            AddRef();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("HostWrapper.Destroy");
        }

        /// <summary>
        /// Add service instance factory
        /// </summary>
        /// <param name="serviceId">guid</param>
        /// <param name="factory">factory delegate</param>
        public void AddServiceWrapper(in Guid serviceId, Func<COMCallableIUnknown> factory)
        {
            _factories.Add(serviceId, factory);
        }

        /// <summary>
        /// Add service instance
        /// </summary>
        /// <param name="serviceId">guid</param>
        /// <param name="service">instance</param>
        public void AddServiceWrapper(in Guid serviceId, COMCallableIUnknown service)
        {
            _wrappers.Add(serviceId, service);
        }

        /// <summary>
        /// Returns the wrapper instance for the guid
        /// </summary>
        /// <param name="serviceId">service guid</param>
        /// <returns>instance or null</returns>
        public COMCallableIUnknown GetServiceWrapper(in Guid serviceId)
        {
            if (!_wrappers.TryGetValue(serviceId, out COMCallableIUnknown service))
            {
                if (_factories.TryGetValue(serviceId, out Func<COMCallableIUnknown> factory))
                {
                    service = factory();
                    if (service != null)
                    {
                        _wrappers.Add(serviceId, service);
                    }
                }
            }
            return service;
        }

        public void DestroyTarget()
        {
            if (_targetWrapper != null)
            {
                _targetWrapper.Release();
                _targetWrapper = null;
            }
        }

        #region IHost

        /// <summary>
        /// Returns the host type
        /// </summary>
        private HostType GetHostType(IntPtr self) => _host.HostType;

        /// <summary>
        /// Returns the native service for the given interface id. There is 
        /// only a limited set of services that can be queried through this
        /// function. Adds a reference like QueryInterface.
        /// </summary>
        /// <param name="serviceId">guid of the service</param>
        /// <param name="service">pointer to return service instance</param>
        /// <returns>S_OK or E_NOINTERFACE</returns>
        private HResult GetService(IntPtr self, in Guid guid, out IntPtr ptr)
        {
            ptr = IntPtr.Zero;

            COMCallableIUnknown wrapper = GetServiceWrapper(guid);
            if (wrapper == null)
            {
                return HResult.E_NOINTERFACE;
            }
            wrapper.AddRef();
            return COMHelper.QueryInterface(wrapper.IUnknownObject, guid, out ptr);
        }

        /// <summary>
        /// Returns the current target wrapper or null
        /// </summary>
        /// <param name="target">target wrapper address returned</param>
        /// <returns>S_OK</returns>
        private HResult GetCurrentTarget(IntPtr self, out IntPtr target)
        {
            target = IntPtr.Zero;

            if (_host.CurrentTarget == null) {
                return HResult.E_NOINTERFACE;
            }
            // TODO: this only supports one target instance. Need to add a dictionary lookup for multi-target support.
            if (_targetWrapper == null) {
                _targetWrapper = new TargetWrapper(_host.CurrentTarget);
            }
            _targetWrapper.AddRef();
            target = _targetWrapper.ITarget;

            return HResult.S_OK;
        }

        #endregion

        #region IHost delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HostType GetHostTypeDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult GetServiceDelegate(
            [In] IntPtr self,
            [In] in Guid guid,
            [Out] out IntPtr ptr);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate HResult GetCurrentTargetDelegate(
            [In] IntPtr self,
            [Out] out IntPtr target);

        #endregion
    }
}
