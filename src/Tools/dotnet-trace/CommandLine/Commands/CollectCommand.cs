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
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class CollectCommandHandler
    {
        public static async Task<int> Collect(IConsole console, int pid, string output, uint buffersize, string providers)
        {
            try
            {
                if (output == null)
                    throw new ArgumentNullException(nameof(output));

                var configuration = new SessionConfiguration(
                    circularBufferSizeMB: buffersize,
                    outputPath: null, // Not used on the streaming scenario.
                    Extensions.ToProviders(providers));

                ulong sessionId = 0;
                using (Stream stream = EventPipeClient.CollectTracing(pid, configuration, out sessionId))
                {
                    if (sessionId == 0)
                    {
                        Console.Error.WriteLine("Unable to create session.");
                        return -1;
                    }

                    Console.Out.WriteLine("press CTRL+C to quit ...");
                    using (var fs = new FileStream(output, FileMode.Create, FileAccess.Write))
                    {
                        Console.Out.WriteLine($"Recording tracing session to: {fs.Name}");
                        Console.Out.WriteLine($"  SessionId: 0x{sessionId:X16}");

                        while (true)
                        {
                            var buffer = new byte[16 * 1024];
                            int nBytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (nBytesRead <= 0)
                                break;
                            fs.Write(buffer, 0, nBytesRead);


                            // TODO: Units should scale w.r.t. file size.
                            Console.Out.Write($"\r  Recording trace {fs.Length} (bytes)");
                            Debug.WriteLine($"PACKET: {Convert.ToBase64String(buffer, 0, nBytesRead)} (bytes {nBytesRead})");
                        }
                    }
                }

                Console.Out.WriteLine();
                Console.Out.WriteLine("Trace completed.");

                // This is validating output.
                if (sessionId != 0)
                {
                    var eventPipeResults = new List<TraceEvent>();
                    using (var trace = new TraceLog(TraceLog.CreateFromEventPipeDataFile(output)).Events.GetSource())
                    {
                        trace.Dynamic.All += (TraceEvent data) => {
                            eventPipeResults.Add(data);
                        };

                        trace.Process();
                    }

                    eventPipeResults.ForEach(e => {
                        if (!string.IsNullOrWhiteSpace(e.ProviderName) && !string.IsNullOrWhiteSpace(e.EventName))
                        {
                            Debug.WriteLine($"Event Provider: {e.ProviderName}");
                            Debug.WriteLine($"    Event Name: {e.EventName}");
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

        public static Command CollectCommand() =>
            new Command(
                name: "collect",
                description: "Starts an EventPipe tracing session.",
                symbols: new Option[] {
                    CommonOptions.ProcessIdOption(),
                    CommonOptions.CircularBufferOption(),
                    CommonOptions.OutputPathOption(),
                    CommonOptions.ProvidersOption(),
                },
                handler: CommandHandler.Create<IConsole, int, string, uint, string>(Collect));
    }
}
