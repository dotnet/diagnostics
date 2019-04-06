// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
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
                    outputPath: output,
                    Provider.ToProviders(providers));
                string filePath = null;
                ulong sessionId = 0;

                using (Stream stream = EventPipeClient.StreamTracingToFile(pid, configuration, out sessionId))
                {
                    if (sessionId == 0)
                    {
                        Console.Error.WriteLine("Unable to create streaming session.");
                        return -1;
                    }

                    filePath = $"dotnetcore-eventpipe-{pid}-0x{sessionId:X16}.netperf";
                    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                    {
                        Console.Out.WriteLine($"OutputPath={fs.Name}");
                        Console.Out.WriteLine($"SessionId=0x{sessionId:X16}");

                        while (true)
                        {
                            var buffer = new byte[1024];
                            int nBytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (nBytesRead <= 0)
                                break;
                            Console.WriteLine($"PACKET: {Convert.ToBase64String(buffer, 0, nBytesRead)}");
                            fs.Write(buffer, 0, nBytesRead);
                        }
                    }
                }

                if (sessionId != 0 && filePath != null)
                {
                    var eventPipeResults = new List<TraceEvent>();
                    using (var trace = new TraceLog(TraceLog.CreateFromEventPipeDataFile(filePath)).Events.GetSource())
                    {
                        trace.Dynamic.All += (TraceEvent data) => {
                            eventPipeResults.Add(data);
                        };

                        trace.Process();
                    }

                    eventPipeResults.ForEach(e => {
                        if (!string.IsNullOrWhiteSpace(e.ProviderName) && !string.IsNullOrWhiteSpace(e.EventName))
                        {
                            Console.Out.WriteLine($"Event Provider: {e.ProviderName}");
                            Console.Out.WriteLine($"    Event Name: {e.EventName}");
                        }
                    });
                }

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
    }
}
