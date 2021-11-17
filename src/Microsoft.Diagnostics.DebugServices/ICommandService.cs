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
        /// Enumerates all the command's name and help
        /// </summary>
        IEnumerable<(string name, string help, IEnumerable<string> aliases)> Commands { get; }

        /// <summary>
        /// Add the commands and aliases attributes found in the type.
        /// </summary>
        /// <param name="type">Command type to search</param>
        /// <param name="factory">function to create command instance</param>
        void AddCommands(Type type, Func<IServiceProvider, object> factory);
    }
}
