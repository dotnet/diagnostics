// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class StartCommandHandler
    {
        public static async Task<int> Start(IConsole console, int pid, string output, uint buffersize, string providers)
        {
            try
            {
                var configuration = new SessionConfiguration(
                    circularBufferSizeMB: buffersize,
                    outputPath: output,
                    Extensions.ToProviders(providers));
                var sessionId = EventPipeClient.EnableTracingToFile(pid, configuration);
                Console.Out.WriteLine($"OutputPath={configuration.OutputPath}");
                Console.Out.WriteLine($"SessionId=0x{sessionId:X16}");

                await Task.FromResult(0);
                return sessionId != 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                return 1;
            }
        }

        public static Command StartCommand() =>
            new Command(
                name: "start",
                description: "Starts an EventPipe session where the runtime writes to a file.",
                symbols: new Option[] {
                    CommonOptions.ProcessIdOption(),
                    CommonOptions.OutputPathOption(),
                    CommonOptions.CircularBufferOption(),
                    CommonOptions.ProvidersOption(),
                },
                handler: CommandHandler.Create<IConsole, int, string, uint, string>(Start));
    }
}
