// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Tools.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    [Flags]
    internal enum SinkType
    {
        None = 0,
        Console = 1,
        All = 0xff
    }

    class Program
    {
        private static Command CollectCommand() =>
              new Command(
                  name: "collect",
                  description: "Monitor logs and metrics in a .NET application send the results to a chosen destination.")
              {
                // Handler
                CommandHandler.Create<CancellationToken, IConsole, string[]>(new DiagnosticsMonitorCommandHandler().Start),
                PortOption()
              };

        private static Option PortOption() =>
            new Option(
                aliases: new[] { "-u", "--urls"},
                description: "Bindings for the REST api.")
            {
                Argument = new Argument<string[]>(name: "urls", defaultValue: new[] { "http://localhost:52323" })
            };

        public static Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                            .AddCommand(CollectCommand())
                            .UseDefaults()
                            .Build();
            return parser.InvokeAsync(args);
        }
    }
}
