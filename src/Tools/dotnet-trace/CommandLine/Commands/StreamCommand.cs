// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class StreamCommandHandler
    {
        public static async Task<int> Stream(IConsole console, int pid, string output, uint buffersize, string providers)
        {
            try
            {
                var configuration = new SessionConfiguration(
                    circularBufferSizeMB: buffersize,
                    multiFileSec: 0,
                    outputPath: output,
                    ToProviders(providers));
                var binaryReader = EventPipeClient.StreamTracingToFile(pid, configuration, out var sessionId);
                Console.Out.WriteLine($"SessionId=0x{sessionId:X16}");

                if (sessionId != 0)
                {
                    var filePath = $"dotnetcore-eventpipe-{pid}-0x{sessionId:X16}.netperf";
                    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                    {
                        while (true)
                        {
                            var buffer = new byte[1024];
                            int nBytesRead = binaryReader.Read(buffer, 0, buffer.Length);
                            if (nBytesRead <= 0)
                                break;
                            fs.Write(buffer, 0, nBytesRead);
                        }
                    }
                }

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
                name: "stream",
                description: "Starts an EventPipe session.",
                symbols: new Option[] {
                    CommonOptions.ProcessIdOption(),
                    CircularBufferOption(),
                    ProvidersOption(),
                },
                handler: CommandHandler.Create<IConsole, int, string, uint, string>(Stream));

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

        private static IEnumerable<Provider> ToProviders(string providers)
        {
            if (string.IsNullOrWhiteSpace(providers))
                throw new ArgumentNullException(nameof(providers));
            return providers.Split(',')
                .Select(Provider.ToProvider);
        }
    }
}
