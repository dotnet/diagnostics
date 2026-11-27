// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;

namespace SOS.Extensions
{
    internal sealed class DebuggerComService<T> : IDisposable where T : class
    {
        private T _proxy;

        private DebuggerComService(T proxy) => _proxy = proxy;

        internal static unsafe T Register(ServiceContainer services, IntPtr debugger)
        {
            Guid iid = typeof(T).GUID;
            if (Marshal.QueryInterface(debugger, in iid, out IntPtr service) < 0)
            {
                return null;
            }

            T proxy;
            try
            {
                proxy = UniqueComInterfaceMarshaller<T>.ConvertToManaged((void*)service);
            }
            finally
            {
                Marshal.Release(service);
            }

            DebuggerComService<T> owner = new(proxy);
            try
            {
                services.AddService(owner);
            }
            catch
            {
                owner.Dispose();
                throw;
            }

            // Consumers borrow the interface; the container disposes its registered owner.
            services.AddService(proxy);
            return proxy;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _proxy, null) is ComObject proxy)
            {
                ComWrapper.FinalReleaseUniqueProxy(proxy);
            }
        }
    }
}
