// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS.Hosting
{
    public sealed class HostWrapper : COMCallableIUnknown
    {
        private static readonly Guid IID_IHost = new Guid("E0CD8534-A88B-40D7-91BA-1B4C925761E9");

        private readonly IHost _host;
        private readonly Func<TargetWrapper> _getTarget;
        private readonly Dictionary<Guid, Func<COMCallableIUnknown>> _factories = new Dictionary<Guid, Func<COMCallableIUnknown>>();
        private readonly Dictionary<Guid, COMCallableIUnknown> _wrappers = new Dictionary<Guid, COMCallableIUnknown>();

        public IntPtr IHost { get; }

        public HostWrapper(IHost host, Func<TargetWrapper> getTarget)
        {
            _host = host;
            _getTarget = getTarget;

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
            foreach (COMCallableIUnknown wrapper in _wrappers.Values)
            {
                wrapper.Release();
            }
            _wrappers.Clear();
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
        /// Remove the service instance
        /// </summary>
        /// <param name="serviceId">guid</param>
        public void RemoveServiceWrapper(in Guid serviceId)
        {
            _factories.Remove(serviceId);
            _wrappers.Remove(serviceId);
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
        /// <param name="targetWrapper">target wrapper address returned</param>
        /// <returns>S_OK</returns>
        private HResult GetCurrentTarget(IntPtr self, out IntPtr targetWrapper)
        {
            TargetWrapper wrapper = _getTarget();
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
