// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting;

namespace SOS.FrameworkHost
{
    public static class Host
    {
        private static readonly object s_lock = new();
        private static AppDomain s_domain;

        public static int Initialize(string extensionPath)
        {
            try
            {
                lock (s_lock)
                {
                    string bootstrapPath = typeof(Host).Assembly.Location;
                    if (s_domain == null)
                    {
                        string directory = Path.GetDirectoryName(bootstrapPath);
                        AppDomainSetup setup = new()
                        {
                            ApplicationBase = directory,
                            ConfigurationFile = Path.Combine(directory, "SOS.Extensions.dll.config"),
                            LoaderOptimization = LoaderOptimization.SingleDomain
                        };
                        // Native SOS retains managed callback pointers, so the domain must outlive every debugger session.
                        s_domain = AppDomain.CreateDomain("SOS.Framework.Isolated", null, setup);
                    }

                    // Hosting and remoting can load this assembly in different contexts. Share only an mscorlib interface.
                    IObjectHandle initializer = (IObjectHandle)s_domain.CreateInstanceFromAndUnwrap(
                        bootstrapPath, typeof(Initializer).FullName, false,
                        BindingFlags.Public | BindingFlags.Instance, null,
                        new object[] { extensionPath }, null, null);
                    return (int)initializer.Unwrap();
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"SOS Framework host initialization failed: {ex}");
                return ex.HResult;
            }
        }
    }

    public sealed class Initializer : MarshalByRefObject, IObjectHandle
    {
        private readonly int _result;

        public Initializer(string extensionPath)
        {
            try
            {
                Assembly assembly = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SOS.Extensions.dll"));
                Type hostServices = assembly.GetType("SOS.Extensions.HostServices", throwOnError: true);
                MethodInfo initialize = hostServices.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
                _result = (int)initialize.Invoke(null, new object[] { extensionPath });
            }
            catch (Exception ex)
            {
                // Do not marshal SOS exception types (and their dependencies) back into the debugger's AppDomain.
                Exception failure = ex is TargetInvocationException invocation ? invocation.InnerException : ex;
                Trace.TraceError($"SOS initialization failed: {failure}");
                _result = failure.HResult;
            }
        }

        public object Unwrap() => _result;

        public override object InitializeLifetimeService() => null;
    }
}
