// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SOS.Hosting
{
    public sealed class ServiceWrapper : IDisposable
    {
        private readonly Dictionary<Guid, Func<COMCallableIUnknown>> _factories = new Dictionary<Guid, Func<COMCallableIUnknown>>();
        private readonly Dictionary<Guid, COMCallableIUnknown> _wrappers = new Dictionary<Guid, COMCallableIUnknown>();

        public ServiceWrapper()
        {
        }

        public void Dispose()
        {
            Trace.TraceInformation("ServiceWrapper.Dispose");
            foreach (var wrapper in _wrappers.Values)
            {
                wrapper.ReleaseWithCheck();
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

        /// <summary>
        /// Returns the native service for the given interface id. There is 
        /// only a limited set of services that can be queried through this
        /// function. Adds a reference like QueryInterface.
        /// </summary>
        /// <param name="serviceId">guid of the service</param>
        /// <param name="service">pointer to return service instance</param>
        /// <returns>S_OK or E_NOINTERFACE</returns>
        public int GetService(IntPtr self, in Guid guid, out IntPtr ptr)
        {
            ptr = IntPtr.Zero;

            COMCallableIUnknown wrapper = GetServiceWrapper(guid);
            if (wrapper == null) {
                return HResult.E_NOINTERFACE;
            }
            return COMHelper.QueryInterface(wrapper.IUnknownObject, guid, out ptr);
        }
    }
}
