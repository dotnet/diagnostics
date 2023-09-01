// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Invocation;

namespace Microsoft.Tools.Common
{
    public static class CommandExtenions
    {
        /// <summary>
        /// Allows the command handler to be included in the collection initializer.
        /// </summary>
        public static void Add(this Command command, ICommandHandler handler)
        {
            command.Handler = handler;
        }
    }
}
