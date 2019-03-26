using Microsoft.Diagnostics.Tools.RuntimeClient.Eventing;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics.Tracing;
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

                using (var fs = new FileStream("foo.netperf", FileMode.Create, FileAccess.Write))
                {
                    while (true)
                    {
                        var buffer = new byte[1024];
                        int read = binaryReader.Read(buffer, 0, buffer.Length);
                        if (read <= 0)
                            break;
                        fs.Write(buffer, 0, read);
                    }
                }

                //using (var trace = new TraceLog(TraceLog.CreateFromEventPipeDataFile("foo.netperf")).Events.GetSource())
                //{
                //    trace.Dynamic.All += delegate (TraceEvent data) {
                //        Console.WriteLine(data);
                //    };
                //    trace.Process();
                //}

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
