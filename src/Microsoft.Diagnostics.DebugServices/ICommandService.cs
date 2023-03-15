// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Command processor service
    /// </summary>
    public interface ICommandService
    {
        /// <summary>
        /// Enumerates all the command's name, help and aliases
        /// </summary>
        IEnumerable<(string name, string help, IEnumerable<string> aliases)> Commands { get; }

        /// <summary>
        /// Add the commands and aliases attributes found in the type.
        /// </summary>
        /// <param name="type">Command type to search</param>
        void AddCommands(Type type);

        /// <summary>
        /// Gets the all the command help
        /// </summary>
        /// <param name="services">service provider</param>
        /// <returns>command invocation and help enumeration</returns>
        public IEnumerable<(string Invocation, string Help)> GetHelp(IServiceProvider services);

        /// <summary>
        /// Displays the detailed help for a command
        /// </summary>
        /// <param name="commandName">name of the command or alias</param>
        /// <param name="services">service provider</param>
        /// <param name="consoleWidth">the width to format the help or int.MaxValue</param>
        /// <returns>help text or null if not found</returns>
        string GetDetailedHelp(string commandName, IServiceProvider services, int consoleWidth);
    }
}
