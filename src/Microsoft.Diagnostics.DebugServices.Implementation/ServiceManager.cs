// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Linq.Expressions;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// The service manager registers any ServiceExportAttribute on types and methods and sets properties,
    /// fields and methods marked  with the ServiceImportAttribute that match the provided services. Tracks 
    /// any unresolved service requests and injects them when the service is registered.
    /// </summary>
    public class ServiceManager : IServiceManager
    {
        private readonly Dictionary<Type, List<ServiceFactory>>[] _factories;
        private readonly List<ServiceContainer>[] _serviceContainers;
        private readonly ServiceEvent<Assembly> _notifyExtensionLoad;

        /// <summary>
        /// This event fires when an extension assembly is loaded
        /// </summary>
        public IServiceEvent<Assembly> NotifyExtensionLoad => _notifyExtensionLoad;

        /// <summary>
        /// Create a service manager instance
        /// </summary>
        public ServiceManager()
        {
            _factories = new Dictionary<Type, List<ServiceFactory>>[(int)ServiceScope.Max];
            _serviceContainers = new List<ServiceContainer>[(int)ServiceScope.Max];
            _notifyExtensionLoad = new ServiceEvent<Assembly>();
            for (int i = 0; i < (int)ServiceScope.Max; i++)
            {
                _factories[i] = new Dictionary<Type, List<ServiceFactory>>();
            }
        }

        /// <summary>
        /// Creates a new service provider instance with all the registered factories for the given scope.
        /// </summary>
        /// <param name="scope">global, per-target, per-runtime, etc. service type</param>
        /// <param name="parent">parent service provider to chain</param>
        /// <returns></returns>
        public IServiceContainer CreateServiceContainer(ServiceScope scope, IServiceProvider parent = null)
        {
            var container = new ServiceContainer(parent, _factories[(int)scope]);
            if (scope == ServiceScope.Global || scope == ServiceScope.Context)
            {
                if (_serviceContainers[(int)scope] == null)
                {
                    _serviceContainers[(int)scope] = new List<ServiceContainer>();
                }
                _serviceContainers[(int)scope].Add(container);
            }
            return container;
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
                    if (serviceAttribute.Type is null)
                    {
                        IEnumerable<Type> interfaces = serviceType.GetInterfaces().Where((i) => i != typeof(IDisposable));
                        if (interfaces.Any())
                        {
                            // If the service type on the attributes wasn't defined, add all the interfaces on the implementation class as services.
                            foreach (Type i in interfaces)
                            {
                                if (i != typeof(IDisposable))
                                {
                                    AddServiceFactory(serviceAttribute.Scope, i, factory);
                                }
                            }
                        }
                        else
                        {
                            AddServiceFactory(serviceAttribute.Scope, serviceType, factory);
                        }
                    }
                    else
                    {
                        AddServiceFactory(serviceAttribute.Scope, serviceAttribute.Type, factory);
                    }
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
        /// Add service factory for the specific scope.
        /// </summary>
        /// <typeparam name="T">service type</typeparam>
        /// <param name="scope">global, per-target, per-runtime, etc. service type</param>
        /// <param name="factory">function to create service instance</param>
        public void AddServiceFactory<T>(ServiceScope scope, ServiceFactory factory) => AddServiceFactory(scope, typeof(T), factory);

        /// <summary>
        /// Add service factory for the specific scope.
        /// </summary>
        /// <param name="scope">global, per-target, per-runtime, etc. service type</param>
        /// <param name="serviceType">service type or interface</param>
        /// <param name="factory">function to create service instance</param>
        public void AddServiceFactory(ServiceScope scope, Type serviceType, ServiceFactory factory)
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));

            if (!_factories[(int)scope].TryGetValue(serviceType, out List<ServiceFactory> services))
            {
                services = new List<ServiceFactory>();
                _factories[(int)scope].Add(serviceType, services);
            }
            services.Add(factory);
            if (_serviceContainers[(int)scope] != null)
            {
                foreach (ServiceContainer container in _serviceContainers[(int)scope])
                {
                    container.AddServiceFactory(serviceType, factory);
                }
            }
        }

        /// <summary>
        /// Load any extra extensions in the search path
        /// </summary>
        public void LoadExtensions()
        {
            List<string> extensionPaths = new();
            string diagnosticExtensions = Environment.GetEnvironmentVariable("DOTNET_DIAGNOSTIC_EXTENSIONS");
            if (!string.IsNullOrEmpty(diagnosticExtensions))
            {
                string[] paths = diagnosticExtensions.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
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
    }
}
