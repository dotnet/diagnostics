// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Collects diagnostics module factories advertised by module initializers.
    /// </summary>
    public static class DiagnosticsModuleCatalog
    {
        private static readonly object s_lock = new();
        private static readonly Dictionary<Type, Func<IDiagnosticsModule>> s_factories = new();

        /// <summary>
        /// Registers a factory without constructing the module.
        /// </summary>
        public static void Register<TModule>(Func<TModule> factory) where TModule : class, IDiagnosticsModule
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            lock (s_lock)
            {
                s_factories.Add(typeof(TModule), factory);
            }
        }

        /// <summary>
        /// Returns a snapshot ordered by assembly-qualified module name.
        /// Each factory creates a module for one service manager.
        /// </summary>
        public static IEnumerable<Func<IDiagnosticsModule>> GetFactories()
        {
            lock (s_lock)
            {
                return s_factories.OrderBy(entry => entry.Key.AssemblyQualifiedName, StringComparer.Ordinal)
                    .Select(entry => entry.Value)
                    .ToArray();
            }
        }
    }
}
