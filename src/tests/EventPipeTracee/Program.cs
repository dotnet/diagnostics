// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventPipeTracee
{
    internal static class Program
    {
        private const string AppLoggerCategoryName = "AppLoggerCategory";

        public static int Main(string[] args)
        {
            int pid = Process.GetCurrentProcess().Id;
            string pipeServerName = args.Length > 0 ? args[0] : null;
            if (pipeServerName == null)
            {
                Console.Error.WriteLine($"{pid} EventPipeTracee: no pipe name");
                Console.Error.Flush();
                return -1;
            }
            using NamedPipeClientStream pipeStream = new(pipeServerName);
            bool spinWait10 = args.Length > 2 && "SpinWait10".Equals(args[2], StringComparison.Ordinal);
            string loggerCategory = args[1];

            Console.WriteLine($"{pid} EventPipeTracee: start process");
            Console.Out.Flush();

            // Signal that the tracee has started
            Console.WriteLine($"{pid} EventPipeTracee: connecting to pipe");
            Console.Out.Flush();
            pipeStream.Connect(5 * 60 * 1000);
            Console.WriteLine($"{pid} EventPipeTracee: connected to pipe");
            Console.Out.Flush();

            ServiceCollection serviceCollection = new();
            serviceCollection.AddLogging(builder => {
                builder.AddEventSourceLogger();
                // Set application defined levels
                builder.AddFilter(null, LogLevel.Error); // Default
                builder.AddFilter(AppLoggerCategoryName, LogLevel.Warning);
            });

            using ILoggerFactory loggerFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();
            ILogger customCategoryLogger = loggerFactory.CreateLogger(loggerCategory);
            ILogger appCategoryLogger = loggerFactory.CreateLogger(AppLoggerCategoryName);

            Console.WriteLine($"{pid} EventPipeTracee: {DateTime.UtcNow} Awaiting start");
            Console.Out.Flush();

            // Wait for server to send something
            int input = pipeStream.ReadByte();

            Console.WriteLine($"{pid} {DateTime.UtcNow} Starting test body '{input}'");
            Console.Out.Flush();

            TestBodyCore(customCategoryLogger, appCategoryLogger);

            Console.WriteLine($"{pid} EventPipeTracee: signal end of test data");
            Console.Out.Flush();
            pipeStream.WriteByte(31);

            if (spinWait10)
            {
                DateTime targetDateTime = DateTime.UtcNow.AddSeconds(10);

                long acc = 0;
                while (DateTime.UtcNow < targetDateTime)
                {
                    acc++;
                    if (acc % 10_000_000 == 0)
                    {
                        Console.WriteLine($"{pid} Spin waiting...");
                    }
                }
            }

            Console.WriteLine($"{pid} {DateTime.UtcNow} Awaiting end");
            Console.Out.Flush();

            // Wait for server to send something
            input = pipeStream.ReadByte();

            Console.WriteLine($"{pid} EventPipeTracee {DateTime.UtcNow} Ending remote test process '{input}'");
            return 0;
        }

        // TODO At some point we may want parameters to choose different test bodies.
        private static void TestBodyCore(ILogger customCategoryLogger, ILogger appCategoryLogger)
        {
            //Json data is always converted to strings for ActivityStart events.
            using (IDisposable scope = customCategoryLogger.BeginScope(new Dictionary<string, object> {
                    { "IntValue", "5" },
                    { "BoolValue", "true" },
                    { "StringValue", "test" } }.ToList()))
            {
                customCategoryLogger.LogInformation("Some warning message with {Arg}", 6);
            }

            customCategoryLogger.LogWarning(new EventId(7, "AnotherEventId"), "Another message");

            appCategoryLogger.LogInformation("Information message.");
            appCategoryLogger.LogWarning(new EventId(5, "WarningEventId"), "Warning message.");
            appCategoryLogger.LogError("Error message.");
        }
    }
}
