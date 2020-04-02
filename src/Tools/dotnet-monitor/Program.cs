// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Internal.Common.Commands;
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
                CommandHandler.Create<CancellationToken, IConsole, int, int, SinkType, IEnumerable<FileInfo>, IEnumerable<FileInfo>>(new DiagnosticsMonitorCommandHandler().Start),
                // Arguments and Options
                ProcessIdOption(), RefreshIntervalOption(), SinkOption(), JsonConfigOption(), FileConfigOption()
              };

        private static Option ProcessIdOption() =>
            new Option(
                aliases: new[] { "-p", "--process-id" },
                description: "The process id that will be monitored.")
            {
                Argument = new Argument<int>(name: "processId")
            };

        private static Option RefreshIntervalOption() =>
            new Option(
                alias: "--refresh-interval",
                description: "The number of seconds to delay between updating the counters.")
            {
                Argument = new Argument<int>(name: "refreshInterval", defaultValue: 10)
            };

        private static Option SinkOption() =>
            new Option(
                alias: "--sink",
                description: "Where to send the data")
            {
                Argument = new Argument<SinkType>(name: "sink", defaultValue: SinkType.Console)
            };

        private static Option JsonConfigOption() =>
        new Option(
            alias: "--json-configs",
            description: "Additonal configuration")
        {
            Argument = new Argument<IEnumerable<FileInfo>>(name: "jsonConfigs"),
            Required = false,
        };

        private static Option FileConfigOption() =>
        new Option(
            alias: "--keyfile-configs",
            description: "Additonal configuration")
        {
            Argument = new Argument<IEnumerable<FileInfo>>(name: "keyFileConfigs"),
            Required = false
        };


        public static Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                            .AddCommand(CollectCommand())
                            .AddCommand(ProcessStatusCommandHandler.ProcessStatusCommand("Lists the dotnet processes that can be monitored"))
                            .UseDefaults()
                            .Build();
            return parser.InvokeAsync(args);
        }
    }
}
