// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

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
        private readonly List<object> _extensions;
        private bool _finalized;

        /// <summary>
        /// This event fires when an extension assembly is loaded
        /// </summary>
        public IServiceEvent<Assembly> NotifyExtensionLoad { get; }

        /// <summary>
        /// This event fires when an extension assembly fails
        /// </summary>
        public IServiceEvent<Exception> NotifyExtensionLoadFailure { get; }

        /// <summary>
        /// Enable the assembly resolver on desktop Framework
        /// </summary>
        static ServiceManager()
        {
            if (RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework"))
            {
                AssemblyResolver.Enable();
            }
        }

        /// <summary>
        /// Create a service manager instance
        /// </summary>
        public ServiceManager()
        {
            _factories = new Dictionary<Type, ServiceFactory>[(int)ServiceScope.Max];
            _providerFactories = new Dictionary<Type, List<ServiceFactory>>();
            _extensions = new List<object>();
            NotifyExtensionLoad = new ServiceEvent<Assembly>();
            NotifyExtensionLoadFailure = new ServiceEvent<Exception>();
            for (int i = 0; i < (int)ServiceScope.Max; i++)
            {
                _factories[i] = new Dictionary<Type, ServiceFactory>();
            }
        }

        /// <summary>
        /// Creates a new service container factory with all the registered factories for the given scope.
        /// </summary>
        /// <param name="scope">global, per-target, per-runtime, etc. service type</param>
        /// <param name="parent">parent service services to chain</param>
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
        /// Get the services factories for a type or interface.
        /// </summary>
        /// <param name="providerType">type or interface</param>
        /// <returns>the services factories for the type</returns>
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
        /// <exception cref="FileNotFoundException">assembly or reference not found</exception>
        /// <exception cref="NotSupportedException">not supported</exception>
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
                    ServiceFactory factory = (services) => Utilities.CreateInstance(serviceType, services);
                    AddServiceFactory(serviceAttribute.Scope, serviceAttribute.Type ?? serviceType, factory);
                }
                ProviderExportAttribute providerAttribute = currentType.GetCustomAttribute<ProviderExportAttribute>(inherit: false);
                if (providerAttribute is not null)
                {
                    ServiceFactory factory = (services) => Utilities.CreateInstance(serviceType, services);
                    AddProviderFactory(providerAttribute.Type ?? serviceType, factory);
                }
                // The method or constructor must be static and public
                foreach (MethodInfo methodInfo in currentType.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    serviceAttribute = methodInfo.GetCustomAttribute<ServiceExportAttribute>(inherit: false);
                    if (serviceAttribute is not null)
                    {
                        ServiceFactory factory = (services) => Utilities.CreateInstance(methodInfo, services);
                        AddServiceFactory(serviceAttribute.Scope, serviceAttribute.Type ?? methodInfo.ReturnType, factory);
                    }
                    providerAttribute = currentType.GetCustomAttribute<ProviderExportAttribute>(inherit: false);
                    if (providerAttribute is not null)
                    {
                        ServiceFactory factory = (services) => Utilities.CreateInstance(methodInfo, services);
                        AddProviderFactory(providerAttribute.Type ?? methodInfo.ReturnType, factory);
                    }
                }
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
                NotifyExtensionLoad.Fire(assembly);
            }
            catch (Exception ex) when
                (ex is DiagnosticsException
                 or ArgumentException
                 or NotSupportedException
                 or FileLoadException
                 or FileNotFoundException)
            {
                Trace.TraceError(ex.ToString());
                NotifyExtensionLoadFailure.Fire(new DiagnosticsException($"Extension load failure - {ex.Message} {assembly.Location}", ex));
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
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            if (_finalized)
            {
                throw new InvalidOperationException();
            }
            _factories[(int)scope].Add(serviceType, factory);
        }

        /// <summary>
        /// Add provider factory.
        /// </summary>
        /// <param name="providerType">service type or interface</param>
        /// <param name="factory">function to create provider instance</param>
        public void AddProviderFactory(Type providerType, ServiceFactory factory)
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            if (_finalized)
            {
                throw new InvalidOperationException();
            }
            if (!_providerFactories.TryGetValue(providerType, out List<ServiceFactory> factories))
            {
                _providerFactories.Add(providerType, factories = new List<ServiceFactory>());
            }
            factories.Add(factory);
        }

        /// <summary>
        /// Finalizes the service manager. Loading extensions or adding service factories are not allowed after this call.
        /// </summary>
        public void FinalizeServices() => _finalized = true;

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
                    catch (Exception ex) when
                        (ex is IOException
                         or ArgumentException
                         or BadImageFormatException
                         or UnauthorizedAccessException
                         or System.Security.SecurityException)
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
                // Assembly load contexts are not supported by the desktop framework
                if (RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework"))
                {
                    assembly = Assembly.LoadFile(extensionPath);
                }
                else
                {
                    assembly = UseAssemblyLoadContext(extensionPath);
                }
            }
            catch (Exception ex) when
                (ex is IOException
                 or ArgumentException
                 or InvalidOperationException
                 or BadImageFormatException
                 or System.Security.SecurityException)
            {
                Trace.TraceError(ex.ToString());
                NotifyExtensionLoadFailure.Fire(ex);
            }
            if (assembly is not null)
            {
                RegisterAssembly(assembly);
            }
        }

        /// <summary>
        /// Load the extension using an assembly load context. This needs to be in
        /// a separate method so ExtensionLoadContext class doesn't get referenced
        /// when running on desktop Framework.
        /// </summary>
        /// <param name="extensionPath">extension assembly path</param>
        /// <returns>assembly</returns>
        private Assembly UseAssemblyLoadContext(string extensionPath)
        {
            ExtensionLoadContext extension = new(extensionPath);
            Assembly assembly = extension.LoadFromAssemblyPath(extensionPath);
            if (assembly is not null)
            {
                // This list is just to keep the load context alive
                _extensions.Add(extension);
            }
            return assembly;
        }

        private sealed class ExtensionLoadContext : AssemblyLoadContext
        {
            private static readonly HashSet<string> s_defaultAssemblies = new() {
                "Microsoft.Diagnostics.DebugServices",
                "Microsoft.Diagnostics.DebugServices.Implementation",
                "Microsoft.Diagnostics.ExtensionCommands",
                "Microsoft.Diagnostics.NETCore.Client",
                "Microsoft.Diagnostics.Repl",
                "Microsoft.Diagnostics.Runtime",
                "Microsoft.FileFormats",
                "Microsoft.SymbolStore",
                "SOS.Extensions",
                "SOS.Hosting",
                "SOS.InstallHelper"
            };

            private static readonly string _defaultAssembliesPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            private readonly string _extensionPath;
            private Dictionary<string, string> _extensionPaths;

            public ExtensionLoadContext(string extensionPath)
            {
                _extensionPath = extensionPath;
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                lock (this)
                {
                    if (_extensionPaths == null)
                    {
                        string[] extensionFiles = Directory.GetFiles(Path.GetDirectoryName(_extensionPath), "*.dll");
                        _extensionPaths = new Dictionary<string, string>();
                        foreach (string file in extensionFiles)
                        {
                            _extensionPaths.Add(Path.GetFileNameWithoutExtension(file), file);
                        }
                    }
                }
                if (s_defaultAssemblies.Contains(assemblyName.Name))
                {
                    Assembly assembly = Default.LoadFromAssemblyPath(Path.Combine(_defaultAssembliesPath, assemblyName.Name) + ".dll");
                    if (assemblyName.Version.Major != assembly.GetName().Version.Major)
                    {
                        throw new InvalidOperationException($"Extension assembly reference version not supported for {assemblyName.Name} {assemblyName.Version}");
                    }
                    return assembly;
                }
                else if (_extensionPaths.TryGetValue(assemblyName.Name, out string path))
                {
                    return LoadFromAssemblyPath(path);
                }
                return null;
            }
        }

        /// <summary>
        /// Used to enable app-local assembly unification.
        /// </summary>
        private static class AssemblyResolver
        {
            private static bool s_initialized;

            /// <summary>
            /// Call to enable the assembly resolver for the current AppDomain.
            /// </summary>
            public static void Enable()
            {
                if (!s_initialized)
                {
                    s_initialized = true;
                    AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
                }
            }

            private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
            {
                // apply any existing policy
                AssemblyName referenceName = new(AppDomain.CurrentDomain.ApplyPolicy(args.Name));
                string fileName = referenceName.Name + ".dll";
                string assemblyPath;
                string probingPath;
                Assembly assembly;

                // Look next to requesting assembly
                assemblyPath = args.RequestingAssembly?.Location;
                if (!string.IsNullOrEmpty(assemblyPath))
                {
                    probingPath = Path.Combine(Path.GetDirectoryName(assemblyPath), fileName);
                    Debug.WriteLine($"Considering {probingPath} based on RequestingAssembly");
                    if (Probe(probingPath, referenceName.Version, out assembly))
                    {
                        Debug.WriteLine($"Matched {probingPath} based on RequestingAssembly");
                        return assembly;
                    }
                }

                // Look next to the executing assembly
                assemblyPath = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(assemblyPath))
                {
                    probingPath = Path.Combine(Path.GetDirectoryName(assemblyPath), fileName);
                    Debug.WriteLine($"Considering {probingPath} based on ExecutingAssembly");
                    if (Probe(probingPath, referenceName.Version, out assembly))
                    {
                        Debug.WriteLine($"Matched {probingPath} based on ExecutingAssembly");
                        return assembly;
                    }
                }

                return null;
            }

            /// <summary>
            /// Considers a path to load for satisfying an assembly ref and loads it
            /// if the file exists and version is sufficient.
            /// </summary>
            /// <param name="filePath">Path to consider for load</param>
            /// <param name="minimumVersion">Minimum version to consider</param>
            /// <param name="assembly">loaded assembly</param>
            /// <returns>true if assembly was loaded</returns>
            private static bool Probe(string filePath, Version minimumVersion, out Assembly assembly)
            {
                if (File.Exists(filePath))
                {
                    AssemblyName name = AssemblyName.GetAssemblyName(filePath);
                    if (name.Version >= minimumVersion)
                    {
                        assembly = Assembly.LoadFile(filePath);
                        return true;
                    }
                }
                assembly = null;
                return false;
            }
        }
    }
}
