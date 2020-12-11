// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Tools.Common;
using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
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
        private static Command GenerateApiKeyCommand() =>
            new Command(
                name: "generatekey",
                description: "Generate api key and hash for authentication")
            {
                CommandHandler.Create<CancellationToken, IConsole>(new GenerateApiKeyCommandHandler().GenerateApiKey)
            };

        private static Command CollectCommand() =>
              new Command(
                  name: "collect",
                  description: "Monitor logs and metrics in a .NET application send the results to a chosen destination.")
              {
                // Handler
                CommandHandler.Create<CancellationToken, IConsole, string[], string[], bool, string, bool>(new DiagnosticsMonitorCommandHandler().Start),
                Urls(), MetricUrls(), ProvideMetrics(), DiagnosticPort(), NoAuth()
              };

        private static Option Urls() =>
            new Option(
                aliases: new[] { "-u", "--urls" },
                description: "Bindings for the REST api.")
            {
                Argument = new Argument<string[]>(name: "urls", getDefaultValue: () => new[] { "https://localhost:52323" })
            };

        private static Option MetricUrls() =>
            new Option(
                aliases: new[] { "--metricUrls" },
                description: "Bindings for metrics")
            {
                Argument = new Argument<string[]>(name: "metricUrls", getDefaultValue: () => new[] { GetDefaultMetricsEndpoint() })
            };

        private static Option ProvideMetrics() =>
            new Option(
                aliases: new[] { "-m", "--metrics" },
                description: "Enable publishing of metrics")
            {
                Argument = new Argument<bool>(name: "metrics", getDefaultValue: () => true)
            };

        private static Option DiagnosticPort() =>
            new Option(
                alias: "--diagnostic-port",
                description: "The fully qualified path and filename of the diagnostic port to which runtime instances can connect.")
            {
                Argument = new Argument<string>(name: "diagnosticPort")
            };

        private static Option NoAuth() =>
            new Option(
                alias: "--no-auth",
                description: "Turn off authentication."
                )
            {
                Argument = new Argument<bool>(name: "noAuth", getDefaultValue: () => false)
            };

        private static string GetDefaultMetricsEndpoint()
        {
            string endpoint = "http://localhost:52325";
            if (RuntimeInfo.IsInDockerContainer)
            {
                //Necessary for prometheus scraping
                endpoint = "http://*:52325";
            }
            return endpoint;
        }

        public static Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                .AddCommand(CollectCommand())
                .AddCommand(GenerateApiKeyCommand())
                .UseDefaults()
                .Build();
            return parser.InvokeAsync(args);
        }
    }
}
