using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Grape
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                return;
            }

            var pathToExe = args[0];
            var pathToConfig = args[1];
            TraceGeneratorConfiguration traceConfig;

            try
            {
                traceConfig = JsonSerializer.Deserialize<TraceGeneratorConfiguration>(File.ReadAllText(pathToConfig));
                var providers = new List<EventPipeProvider>();

                foreach (var configProvider in traceConfig.eventProviders)
                {
                    providers.Add(ToEventPipeProvider(configProvider));
                }
                var eventpipeTracer = new EventPipeTraceGenerator(pathToExe, $"{traceConfig.traceName}.nettrace", providers);
                Console.WriteLine("Collecting EventPipe trace");
                eventpipeTracer.Collect(traceConfig.duration);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var etwTracer = new EtwTraceGenerator(pathToExe, $"{traceConfig.traceName}.etl", providers);
                    Console.WriteLine("Collecting ETW trace");
                    etwTracer.Collect(traceConfig.duration);
                }

                // TODO: Add Linux/LTTng trace generation here once I figure out how to make perfcollect more friendly...
                Console.WriteLine("Done!");         
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to parse the trace configuration file {pathToConfig}");
                Console.WriteLine(e.ToString());
            }
        }

        private static EventPipeProvider ToEventPipeProvider(EventProvider configProvider)
        {
            return new EventPipeProvider(
                configProvider.Name,
                (EventLevel)configProvider.Level,
                configProvider.Keywords.Length > 2 && configProvider.Keywords.StartsWith("0x") ? Convert.ToInt64(configProvider.Keywords.Substring(2), 16) : Convert.ToInt64(configProvider.Keywords, 10),
                ParseArgumentString(configProvider.Arguments)
            );
        }


        private static Dictionary<string, string> ParseArgumentString(string argument)
        {
            if (argument == "")
            {
                return null;
            }
            var argumentDict = new Dictionary<string, string>();

            int keyStart = 0;
            int keyEnd = 0;
            int valStart = 0;
            int valEnd = 0;
            int curIdx = 0;
            bool inQuote = false;
            foreach (var c in argument)
            {
                if (inQuote)
                {
                    if (c == '\"')
                    {
                        inQuote = false;
                    }
                }
                else
                {
                    if (c == '=')
                    {
                        keyEnd = curIdx;
                        valStart = curIdx + 1;
                    }
                    else if (c == ';')
                    {
                        valEnd = curIdx;
                        argumentDict.Add(argument.Substring(keyStart, keyEnd - keyStart), argument.Substring(valStart, valEnd - valStart));
                        keyStart = curIdx + 1; // new key starts
                    }
                    else if (c == '\"')
                    {
                        inQuote = true;
                    }
                }
                curIdx += 1;
            }
            string key = argument.Substring(keyStart, keyEnd - keyStart);
            string val = argument.Substring(valStart);
            argumentDict.Add(key, val);
            return argumentDict;
        }
    }
}
