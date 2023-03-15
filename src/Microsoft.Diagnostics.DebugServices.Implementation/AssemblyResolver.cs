// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Used to enable app-local assembly unification.
    /// </summary>
    public static class AssemblyResolver
    {
        private static readonly string _defaultAssembliesPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private static bool _initialized;

        /// <summary>
        /// Call to enable the assembly resolver for the current AppDomain.
        /// </summary>
        public static void Enable()
        {
            if (!_initialized)
            {
                _initialized = true;
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
            probingPath = Path.Combine(_defaultAssembliesPath, fileName);
            Debug.WriteLine($"Considering {probingPath} based on ExecutingAssembly");
            if (Probe(probingPath, referenceName.Version, out assembly))
            {
                Debug.WriteLine($"Matched {probingPath} based on ExecutingAssembly");
                return assembly;
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
