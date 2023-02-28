// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Diagnostics;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// The service manager registers any ServiceExportAttribute on types and methods and sets properties,
    /// fields and methods marked  with the ServiceImportAttribute that match the provided services. Tracks 
    /// any unresolved service requests and injects them when the service is registered.
    /// </summary>
    public class ServiceManager : IServiceManager
    {
        private readonly Dictionary<Type, ServiceFactory>[] _factories;
        private readonly Dictionary<Type, List<ServiceFactory>> _providerFactories;
        private readonly ServiceEvent<Assembly> _notifyExtensionLoad;
        private bool _finalized;

        /// <summary>
        /// This event fires when an extension assembly is loaded
        /// </summary>
        public IServiceEvent<Assembly> NotifyExtensionLoad => _notifyExtensionLoad;

        /// <summary>
        /// Create a service manager instance
        /// </summary>
        public ServiceManager()
        {
            _factories = new Dictionary<Type, ServiceFactory>[(int)ServiceScope.Max];
            _providerFactories = new Dictionary<Type, List<ServiceFactory>>();
            _notifyExtensionLoad = new ServiceEvent<Assembly>();
            for (int i = 0; i < (int)ServiceScope.Max; i++)
            {
                _factories[i] = new Dictionary<Type, ServiceFactory>();
            }
        }

        /// <summary>
        /// Creates a new service container factory with all the registered factories for the given scope.
        /// </summary>
        /// <param name="scope">global, per-target, per-runtime, etc. service type</param>
        /// <param name="parent">parent service provider to chain</param>
        /// <returns></returns>
        public ServiceContainerFactory CreateServiceContainerFactory(ServiceScope scope, IServiceProvider parent)
        {
            if (!_finalized)
            {
                throw new InvalidOperationException();
            }

            return new ServiceContainerFactory(parent, _factories[(int)scope]);
        }

        /// <summary>
        /// Get the provider factories for a type or interface.
        /// </summary>
        /// <param name="providerType">type or interface</param>
        /// <returns>the provider factories for the type</returns>
        public IEnumerable<ServiceFactory> EnumerateProviderFactories(Type providerType)
        {
            if (!_finalized)
            {
                throw new InvalidOperationException();
            }

            if (_providerFactories.TryGetValue(providerType, out List<ServiceFactory> factories))
            {
                return factories;
            }
            return Array.Empty<ServiceFactory>();
        }

        /// <summary>
        /// Finds all the ServiceExport attributes in the assembly and registers.
        /// </summary>
        /// <param name="assembly">service implementation assembly</param>
        public void RegisterExportedServices(Assembly assembly)
        {
            foreach (Type serviceType in assembly.GetExportedTypes())
            {
                if (serviceType.IsClass)
                {
                    RegisterExportedServices(serviceType);
                }
            }
        }

        /// <summary>
        /// Finds all the ServiceExport attributes in the type and registers.
        /// </summary>
        /// <param name="serviceType">service implementation type</param>
        public void RegisterExportedServices(Type serviceType)
        {
            if (_finalized)
            {
                throw new InvalidOperationException();
            }

            for (Type currentType = serviceType; currentType is not null; currentType = currentType.BaseType)
            {
                if (currentType == typeof(object) || currentType == typeof(ValueType))
                {
                    break;
                }
                ServiceExportAttribute serviceAttribute = currentType.GetCustomAttribute<ServiceExportAttribute>(inherit: false);
                if (serviceAttribute is not null)
                {
                    ServiceFactory factory = (provider) => Utilities.CreateInstance(serviceType, provider);
                    AddServiceFactory(serviceAttribute.Scope, serviceAttribute.Type ?? serviceType, factory);
                }
                // The method or constructor must be static and public
                foreach (MethodInfo methodInfo in currentType.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    serviceAttribute = methodInfo.GetCustomAttribute<ServiceExportAttribute>(inherit: false);
                    if (serviceAttribute is not null)
                    {
                        AddServiceFactory(serviceAttribute.Scope, serviceAttribute.Type ?? methodInfo.ReturnType, (provider) => Utilities.CreateInstance(methodInfo, provider));
                    }
                }
            }
        }

        /// <summary>
        /// Add service containerFactory for the specific scope.
        /// </summary>
        /// <typeparam name="T">service type</typeparam>
        /// <param name="scope">global, per-target, per-runtime, etc. service type</param>
        /// <param name="factory">function to create service instance</param>
        public void AddServiceFactory<T>(ServiceScope scope, ServiceFactory factory) => AddServiceFactory(scope, typeof(T), factory);

        /// <summary>
        /// Add service containerFactory for the specific scope.
        /// </summary>
        /// <param name="scope">global, per-target, per-runtime, etc. service type</param>
        /// <param name="serviceType">service type or interface</param>
        /// <param name="factory">function to create service instance</param>
        public void AddServiceFactory(ServiceScope scope, Type serviceType, ServiceFactory factory)
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (_finalized)
            {
                throw new InvalidOperationException();
            }

            if (scope == ServiceScope.Provider)
            {
                if (!_providerFactories.TryGetValue(serviceType, out List<ServiceFactory> factories))
                {
                    _providerFactories.Add(serviceType, factories = new List<ServiceFactory>());
                }
                factories.Add(factory);
            }
            else
            {
                _factories[(int)scope].Add(serviceType, factory);
            }
        }

        /// <summary>
        /// Load any extra extensions in the search path
        /// </summary>
        public void LoadExtensions()
        {
            if (_finalized)
            {
                throw new InvalidOperationException();
            }

            List<string> extensionPaths = new();
            string diagnosticExtensions = Environment.GetEnvironmentVariable("DOTNET_DIAGNOSTIC_EXTENSIONS");
            if (!string.IsNullOrEmpty(diagnosticExtensions))
            {
                string[] paths = diagnosticExtensions.Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
                extensionPaths.AddRange(paths);
            }
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(assemblyPath))
            {
                string searchPath = Path.Combine(Path.GetDirectoryName(assemblyPath), "extensions");
                if (Directory.Exists(searchPath))
                {
                    try
                    {
                        string[] extensionFiles = Directory.GetFiles(searchPath, "*.dll");
                        extensionPaths.AddRange(extensionFiles);
                    }
                    catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                    {
                        Trace.TraceError(ex.ToString());
                    }
                }
            }
            foreach (string extensionPath in extensionPaths)
            {
                LoadExtension(extensionPath);
            }
        }

        /// <summary>
        /// Load extension from the path
        /// </summary>
        /// <param name="extensionPath">extension assembly path</param>
        public void LoadExtension(string extensionPath)
        {
            if (_finalized)
            {
                throw new InvalidOperationException();
            }

            Assembly assembly = null;
            try
            {
                assembly = Assembly.LoadFrom(extensionPath);
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is BadImageFormatException || ex is System.Security.SecurityException)
            {
                Trace.TraceError(ex.ToString());
            }
            if (assembly is not null)
            {
                RegisterAssembly(assembly);
            }
        }

        /// <summary>
        /// Register the exported services in the assembly and notify the assembly has loaded.
        /// </summary>
        /// <param name="assembly">extension assembly</param>
        public void RegisterAssembly(Assembly assembly)
        {
            if (_finalized)
            {
                throw new InvalidOperationException();
            }

            try
            {
                RegisterExportedServices(assembly);
                _notifyExtensionLoad.Fire(assembly);
            }
            catch (Exception ex) when (ex is DiagnosticsException || ex is NotSupportedException || ex is FileNotFoundException)
            {
                Trace.TraceError(ex.ToString());
            }
        }

        /// <summary>
        /// Finalizes the service manager. Loading extensions or adding service factories are not allowed after this call.
        /// </summary>
        public void FinalizeServices() => _finalized = true;
    }
}
