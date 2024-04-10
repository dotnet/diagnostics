// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;

namespace Microsoft.Internal.Common
{
    public static class CommandExtensions
    {
        /// <summary>
        /// Allows the command handler to be included in the collection initializer.
        /// </summary>
        public static void Add(this Command command, ICommandHandler handler)
        {
            command.Handler = handler;
        }

        /// <summary>
        /// Setups the diagnostic tools defaults. Like .UseDefault except RegisterWithDotnetSuggest() which
        /// causes problems on Linux systems with R/O /tmp directory.
        /// </summary>
        public static CommandLineBuilder UseToolsDefaults(this CommandLineBuilder builder)
        {
            return builder
                .UseVersionOption()
                .UseHelp()
                .UseEnvironmentVariableDirective()
                .UseParseDirective()
                .UseDebugDirective()
                .UseSuggestDirective()
                .UseTypoCorrections()
                .UseParseErrorReporting()
                .UseExceptionHandler()
                .CancelOnProcessTermination();
        }
    }
}
