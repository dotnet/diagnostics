// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient.Eventing;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class StartCommandHandler
    {
        public static async Task<int> Start(IConsole console, int pid, string output, uint buffersize, string providers, ulong multiFileSec)
        {
            try
            {
                var configuration = new SessionConfiguration(
                    circularBufferSizeMB: buffersize,
                    multiFileSec: multiFileSec,
                    outputPath: output,
                    ToProviders(providers));
                var sessionId = EventPipeClient.EnableTracingToFile(pid, configuration);
                Console.Out.WriteLine($"OutputPath={configuration.OutputPath}");
                Console.Out.WriteLine($"SessionId=0x{sessionId:X16}");

                await Task.FromResult(0);
                return sessionId != 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR]: {ex.ToString()}");
                return 1;
            }
        }

        public static Command StartCommand() =>
            new Command(
                name: "start",
                description: "Starts an EventPipe session.",
                symbols: new Option[] {
                    CommonOptions.ProcessIdOption(),
                    OutputPathOption(),
                    CircularBufferOption(),
                    ProvidersOption(),
                    MultiFileSecOption(),
                },
                handler: CommandHandler.Create<IConsole, int, string, uint, string, ulong>(Start));

        private static Option OutputPathOption() =>
            new Option(
                new[] { "-o", "--output" },
                @"The file name to log events to.",
                new Argument<string> { Name = "filename" });

        private static Option CircularBufferOption() =>
            new Option(
                new[] { "--buffersize" },
                @"Sets the size of the in-memory circular buffer in megabytes.",
                new Argument<uint> { Name = "Size" }); // TODO: 1024 ? Default ?

        private static Option ProvidersOption() =>
            new Option(
                aliases: new[] { "--providers" },
                description: @"A list EventPipe provider to be enabled in the form 'Provider[,Provider]', where Provider is in the form: '(GUID|KnownProviderName)[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'",
                argument: new Argument<string> { Name = "Providers" }); // TODO: Can we specify an actual type?

        private static Option MultiFileSecOption() =>
            new Option(
                aliases: new[] { "--multifilesec" },
                description: @"Enable a file switch timer every 'n' seconds (Default is 0)",
                argument: new Argument<ulong> { Name = "MultiFileSec" });

        private static IEnumerable<Provider> ToProviders(string providers)
        {
            if (string.IsNullOrWhiteSpace(providers))
                throw new ArgumentNullException(nameof(providers));
            return providers.Split(',')
                .Select(Provider.ToProvider);
        }
    }
}
