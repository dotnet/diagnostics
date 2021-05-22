// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
        /// Enumerates all the command's name and help
        /// </summary>
        IEnumerable<(string name, string help, IEnumerable<string> aliases)> Commands { get; }

        /// <summary>
        /// Add the commands and aliases attributes found in the type.
        /// </summary>
        /// <param name="type">Command type to search</param>
        void AddCommands(Type type);

        /// <summary>
        /// Displays the help for a command
        /// </summary>
        /// <param name="commandName">name of the command or alias</param>
        /// <param name="services">service provider</param>
        /// <returns>true if success, false if command not found</returns>
        bool DisplayHelp(string commandName, IServiceProvider services);
    }
}
