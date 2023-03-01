// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class CommandServiceExtensions
    {
        /// <summary>
        /// Add commands from assemblies. Searches for the command and alias attributes in all the assemblies' types.
        /// </summary>
        /// <param name="commandService">command service instance</param>
        /// <param name="assemblies">list of assemblies to search</param>
        public static void AddCommands(this ICommandService commandService, IEnumerable<Assembly> assemblies)
        {
            commandService.AddCommands(assemblies.SelectMany((assembly) => assembly.GetExportedTypes()));
        }

        /// <summary>
        /// Add commands from an assembly. Searches for the command and alias attributes in all the assembly's types.
        /// </summary>
        /// <param name="commandService">command service instance</param>
        /// <param name="assembly">assembly to search for commands</param>
        public static void AddCommands(this ICommandService commandService, Assembly assembly)
        {
            commandService.AddCommands(assembly.GetExportedTypes());
        }

        /// <summary>
        /// Searches for the command and alias attributes in all types.
        /// </summary>
        /// <param name="commandService">command service instance</param>
        /// <param name="types">list of types to search</param>
        public static void AddCommands(this ICommandService commandService, IEnumerable<Type> types)
        {
            foreach (Type type in types)
            {
                commandService.AddCommands(type);
            }
        }
    }
}
