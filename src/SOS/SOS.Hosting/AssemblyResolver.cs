// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace SOS
{
    /// <summary>
    /// Used to enable app-local assembly unification.
    /// </summary>
    public static class AssemblyResolver
    {
        private static bool s_initialized = false;

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
            AssemblyName referenceName = new AssemblyName(AppDomain.CurrentDomain.ApplyPolicy(args.Name));

            if (referenceName.Name == "SOS.NETCore")
            {
                string fileName = referenceName.Name + ".dll";
                string assemblyPath = null;
                string probingPath = null;
                Assembly assembly = null;

                // Look next to requesting assembly
                assemblyPath = args.RequestingAssembly?.Location;
                if (!String.IsNullOrEmpty(assemblyPath))
                {
                    probingPath = Path.Combine(Path.GetDirectoryName(assemblyPath), fileName);
                    Debug.WriteLine($"Considering {probingPath} based on RequestingAssembly");
                    if (Probe(probingPath, referenceName.Version, out assembly)) {
                        return assembly;
                    }
                }

                // Look next to the executing assembly
                assemblyPath = Assembly.GetExecutingAssembly().Location;
                if (!String.IsNullOrEmpty(assemblyPath))
                {
                    probingPath = Path.Combine(Path.GetDirectoryName(assemblyPath), fileName);
                    Debug.WriteLine($"Considering {probingPath} based on ExecutingAssembly");
                    if (Probe(probingPath, referenceName.Version, out assembly)) {
                        return assembly;
                    }
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
                if (name.Version >= minimumVersion) {
                    assembly = Assembly.LoadFile(filePath);
                    return true;
                }
            }
            assembly = null;
            return false;
        }
    }
}
