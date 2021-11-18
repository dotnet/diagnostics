// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.IO;

using Microsoft.Diagnostics.Tools.RuntimeClient;
using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Tools.Counters
{
    internal class Program
    {
        private static ulong _sessionId;
        private static int threshold;
        private static int pid; 


        private static void Main(string[] args)
        {
            if(args.Length<2)
            {
                Console.WriteLine("triggerdump <pid> <mem threshold in MB>");
            }
            else
            {
                    pid = Convert.ToInt32(args[0]);
                    threshold = Convert.ToInt32(args[1]);

                    Task monitorTask = new Task(() => 
                    {
                        var prov = new List<Provider>();
                        prov.Add(new Provider("System.Runtime", filterData:"EventCounterIntervalSec=1"));

                        var configuration = new SessionConfiguration(
                        circularBufferSizeMB: 1000,
                        outputPath: "",
                        providers: prov);
                        
                        var binaryReader = EventPipeClient.CollectTracing(Int32.Parse(args[0]), configuration, out _sessionId);
                        EventPipeEventSource source = new EventPipeEventSource(binaryReader);
                        source.Dynamic.All += Dynamic_All;
                        source.Process();
                    });

                    Task commandTask = new Task(() =>
                    {
                        while(true)
                        {
                            while (!Console.KeyAvailable) { }
                            ConsoleKey cmd = Console.ReadKey(true).Key;
                            if (cmd == ConsoleKey.Q)
                            {
                                break;
                            }
                        }
                    });

                    monitorTask.Start();
                    commandTask.Start();
                    commandTask.Wait();

                    try
                    {
                        EventPipeClient.StopTracing(Int32.Parse(args[0]), _sessionId);    
                    }
                    catch (System.IO.EndOfStreamException) {} 
            }
        }

        private static void Dynamic_All(TraceEvent obj)
        {
            if (obj.EventName.Equals("EventCounters"))
            {
                IDictionary<string, object> payloadVal = (IDictionary<string, object>)(obj.PayloadValue(0));
                IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                ICounterPayload payload = payloadFields.Count == 6 ? (ICounterPayload)new IncrementingCounterPayload(payloadFields) : (ICounterPayload)new CounterPayload(payloadFields);
                string displayName = payload.GetDisplay();                
                if (string.IsNullOrEmpty(displayName))
                {
                    displayName = payload.GetName();
                }

                if(string.Compare(displayName, "GC Heap Size") == 0 && Convert.ToInt32(payload.GetValue())>threshold)
                {
                    Console.WriteLine("Memory threshold has been breached....");
                    System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(pid);

                    System.Diagnostics.ProcessModule coreclr = process.Modules.Cast<System.Diagnostics.ProcessModule>().FirstOrDefault(m => string.Equals(m.ModuleName, "libcoreclr.so"));
                    if (coreclr == null)
                    {
                        Console.WriteLine("Unable to locate .NET runtime associated with this process!");
                        Environment.Exit(1);
                    }
                    else
                    {
                        string runtimeDirectory = Path.GetDirectoryName(coreclr.FileName);
                        string createDumpPath = Path.Combine(runtimeDirectory, "createdump");
                        if (!File.Exists(createDumpPath))
                        {
                            Console.WriteLine("Unable to locate 'createdump' tool in '{runtimeDirectory}'");
                            Environment.Exit(1);                            
                        }                        

                        var createdump = new System.Diagnostics.Process()
                        {       
                            StartInfo = new System.Diagnostics.ProcessStartInfo()
                            {
                                FileName = createDumpPath,
                                Arguments = $"--name coredump --withheap {pid}",
                            },
                            EnableRaisingEvents = true,
                        };

                        createdump.Start();
                        createdump.WaitForExit();

                        Environment.Exit(0);
                    }
                }
            }
        }

    }
}
