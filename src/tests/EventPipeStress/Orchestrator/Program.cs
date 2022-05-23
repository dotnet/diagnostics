using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;

using Common;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

using Process = System.Diagnostics.Process;
using System.Runtime.Versioning;

namespace Orchestrator
{
    public enum ReaderType
    {
        Stream,
        EventPipeEventSource
    }

    class Program
    {
        delegate Task<int> RootCommandHandler(
            IConsole console,
            CancellationToken ct,
            FileInfo stressPath,
            int eventSize,
            int eventRate,
            BurstPattern burstPattern,
            ReaderType readerType,
            int slowReader,
            int duration,
            int cores,
            int threads,
            int eventCount,
            bool rundown,
            int bufferSize,
            int iterations,
            bool pause);

        // TODO: Collect CPU % of reader and writer while running test and add to stats
        // TODO: Standardize and clean up logging from orchestrator and corescaletest
        // TODO: Improve error handling

        static async Task<int> Main(string[] args)
        {

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Try to run in admin mode in Windows
                bool isElevated;
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                if (!isElevated)
                {
                    Console.WriteLine("Must run in root/admin mode");
                    return -1;
                } 
            }

            return await BuildCommandLine()
                            .UseDefaults()
                            .Build()
                            .InvokeAsync(args);
        }

        static CommandLineBuilder BuildCommandLine()
        {
            var rootCommand = new RootCommand("EventPipe Stress Tester - Orchestrator")
            {
                new Argument<FileInfo>(
                    name: "stress-path",
                    description: "The location of the Stress executable."
                ),
                CommandLineOptions.EventSizeOption,
                CommandLineOptions.EventRateOption,
                CommandLineOptions.BurstPatternOption,
                OrchestrateCommandLine.ReaderTypeOption,
                OrchestrateCommandLine.SlowReaderOption,
                CommandLineOptions.DurationOption,
                OrchestrateCommandLine.CoresOption,
                CommandLineOptions.ThreadsOption,
                CommandLineOptions.EventCountOption,
                OrchestrateCommandLine.RundownOption,
                OrchestrateCommandLine.BufferSizeOption,
                OrchestrateCommandLine.IterationsOption,
                OrchestrateCommandLine.PauseOption
            };


            rootCommand.Handler = CommandHandler.Create((RootCommandHandler)Orchestrate);
            return new CommandLineBuilder(rootCommand);
        }

        private static EventPipeSession GetSession(int pid, bool rundown, int bufferSize)
        {
            DiagnosticsClient client = new DiagnosticsClient(pid);
            while (!client.CheckTransport())
            {
                Console.WriteLine("still unable to talk");
                Thread.Sleep(50);
            }
            return client.StartEventPipeSession(
                new EventPipeProvider("MySource", EventLevel.Verbose), 
                requestRundown: rundown, 
                circularBufferMB: bufferSize);
        }

        /// <summary>
        /// This uses EventPipeEventSource's Stream constructor to parse the events real-time.
        /// It then returns the number of events read.
        /// </summary>
        private static Func<int, TestResult> UseEPES(bool rundown, int bufferSize, int slowReader)
        {
            return (int pid) =>
            {
                int eventsRead = 0;
                var slowReadSw = new Stopwatch();
                var totalTimeSw = new Stopwatch();
                var interval = TimeSpan.FromSeconds(0.75);

                EventPipeSession session = GetSession(pid, rundown, bufferSize);
                Console.WriteLine("Session created.");

                EventPipeEventSource epes = new EventPipeEventSource(session.EventStream);
                epes.Dynamic.All += (TraceEvent data) => {
                    eventsRead += 1;
                    if (slowReader > 0)
                    {
                        if (slowReadSw.Elapsed > interval)
                        {
                            Thread.Sleep(slowReader);
                            slowReadSw.Reset();
                        }
                    }
                };
                if (slowReader > 0)
                    slowReadSw.Start();
                totalTimeSw.Start();
                epes.Process();
                totalTimeSw.Stop();
                if (slowReader > 0)
                    slowReadSw.Stop();
                Console.WriteLine("Read total: " + eventsRead.ToString());
                Console.WriteLine("Dropped total: " + epes.EventsLost.ToString());

                return new TestResult(eventsRead, epes.EventsLost, totalTimeSw.Elapsed);
            };
        }

        /// <summary>
        /// This uses CopyTo to copy the trace into a filesystem first, and then uses EventPipeEventSource
        /// on the file to post-process it and return the total # of events read.
        /// </summary>
        static Func<int, TestResult> UseFS(bool rundown, int bufferSize)
        {
            return (int pid) =>
            {
                int eventsRead = 0;
                var totalTimeSw = new Stopwatch();
                const string fileName = "./temp.nettrace";

                EventPipeSession session = GetSession(pid, rundown, bufferSize);
                Console.WriteLine("Session created.");

                using (FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                {
                    totalTimeSw.Start();
                    session.EventStream.CopyTo(fs);
                    totalTimeSw.Stop();
                }
                EventPipeEventSource epes = new EventPipeEventSource(fileName);
                epes.Dynamic.All += (TraceEvent data) => {
                    eventsRead += 1;
                };
                epes.Process();
                Console.WriteLine("Read total: " + eventsRead.ToString());
                Console.WriteLine("Dropped total: " + epes.EventsLost.ToString());

                return new TestResult(eventsRead, epes.EventsLost, totalTimeSw.Elapsed);
            };
        }

        [SupportedOSPlatformGuard("Windows")]
        [SupportedOSPlatformGuard("Linux")]
        static bool IsWindowsOrLinux => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

        static async Task<int> Orchestrate(
            IConsole console,
            CancellationToken ct,
            FileInfo stressPath,
            int eventSize,
            int eventRate,
            BurstPattern burstPattern,
            ReaderType readerType,
            int slowReader,
            int duration,
            int cores,
            int threads,
            int eventCount,
            bool rundown,
            int bufferSize,
            int iterations,
            bool pause)
        {
            if (!stressPath.Exists)
            {
                Console.WriteLine($"");
                return -1;
            }

            string readerTypeString = readerType switch
            {
                ReaderType.Stream => "Stream",
                ReaderType.EventPipeEventSource => "EventPipeEventSource",
                _ => "Stream"
            };

            var durationTimeSpan = TimeSpan.FromSeconds(duration); 
            var testResults = new TestResults(eventSize);

            Func<int, TestResult> threadProc = readerType switch
            {
                ReaderType.Stream => UseFS(rundown, bufferSize),
                ReaderType.EventPipeEventSource => UseEPES(rundown, bufferSize, slowReader),
                _ => throw new ArgumentException("Invalid reader type")
            };

            if (eventRate == -1 && burstPattern != BurstPattern.NONE)
                throw new ArgumentException("Must have burst pattern of NONE if rate is -1");

            Console.WriteLine($"Configuration: event_size={eventSize}, event_rate={eventRate}, cores={cores}, num_threads={threads}, reader={readerType}, event_rate={(eventRate == -1 ? -1 : eventRate * threads)}, burst_pattern={burstPattern.ToString()}, slow_reader={slowReader}, duration={duration}");

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                Console.WriteLine("========================================================");
                Console.WriteLine($"Starting iteration {iteration + 1}");

                Process eventWritingProc = new Process();
                eventWritingProc.StartInfo.FileName = stressPath.FullName;
                eventWritingProc.StartInfo.Arguments = $"--threads {(threads == -1 ? cores.ToString() : threads.ToString())} --event-count {eventCount} --event-size {eventSize} --event-rate {eventRate} --burst-pattern {burstPattern} --duration {(int)durationTimeSpan.TotalSeconds}";
                eventWritingProc.StartInfo.UseShellExecute = false;
                eventWritingProc.StartInfo.RedirectStandardInput = true;
                eventWritingProc.StartInfo.Environment["COMPlus_StressLog"] = "1";
                eventWritingProc.StartInfo.Environment["COMPlus_LogFacility"] = "2000";
                eventWritingProc.StartInfo.Environment["COMPlus_LogLevel"] = "8";
                eventWritingProc.StartInfo.Environment["COMPlus_StressLogSize"] = "0x1000000";
                eventWritingProc.Start();

                Console.WriteLine($"Executing: {eventWritingProc.StartInfo.FileName} {eventWritingProc.StartInfo.Arguments}");

                if (IsWindowsOrLinux)
                {
                    // Set affinity and priority
                    ulong affinityMask = 0;
                    for (int j = 0; j < cores; j++)
                    {
                        affinityMask |= ((ulong)1 << j);
                    }
                    eventWritingProc.ProcessorAffinity = (IntPtr)((ulong)eventWritingProc.ProcessorAffinity & affinityMask);
                }

                // Start listening to the event.
                Task<TestResult> listenerTask = Task.Run(() => threadProc(eventWritingProc.Id), ct);

                if (pause)
                {
                    Console.WriteLine("Press <enter> to start test");
                    Console.ReadLine();
                }

                // start the target process
                StreamWriter writer = eventWritingProc.StandardInput;
                writer.WriteLine("\r\n");
                eventWritingProc.WaitForExit();

                var resultTuple = await listenerTask;
                testResults.Add(resultTuple);

                Console.WriteLine($"Done with iteration {iteration + 1}");
                Console.WriteLine("========================================================");

            }

            Console.WriteLine(testResults.GenerateSummary());
            Console.WriteLine(testResults.GenerateStatisticsTable());

            return 0;
        }
    }
}
