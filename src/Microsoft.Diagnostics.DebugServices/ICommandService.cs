// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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
        /// Registers a command implementation with a custom factory.
        /// </summary>
        /// <typeparam name="TCommand">Command implementation to register</typeparam>
        /// <param name="factory">Function to create a command instance</param>
        void AddCommands<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.PublicProperties)] TCommand>(
            Func<IServiceProvider, TCommand> factory) where TCommand : class;

        /// <summary>
        /// Gets help for all of the commands
        /// </summary>
        /// <param name="services">service provider</param>
        /// <returns>command invocation and help enumeration</returns>
        public IEnumerable<(string Invocation, string Help)> GetAllCommandHelp(IServiceProvider services);

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
