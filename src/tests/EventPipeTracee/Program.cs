﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventPipeTracee
{
    internal static class Program
    {
        private const string AppLoggerCategoryName = "AppLoggerCategory";

        public static async Task<int> Main(string[] args)
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

            bool diagMetrics = args.Any("DiagMetrics".Equals);

            Console.WriteLine($"{pid} EventPipeTracee: DiagMetrics {diagMetrics}");

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

            using CustomMetrics metrics = diagMetrics ? new CustomMetrics() : null;

            // Wait for server to send something
            int input = pipeStream.ReadByte();

            Console.WriteLine($"{pid} {DateTime.UtcNow} Starting test body '{input}'");
            Console.Out.Flush();

            CancellationTokenSource recordMetricsCancellationTokenSource = new();

            if (diagMetrics)
            {
                _ = Task.Run(async () => {

                    // Recording a single value appeared to cause test flakiness due to a race
                    // condition with the timing of when dotnet-counters starts collecting and
                    // when these values are published. Publishing values repeatedly bypasses this problem.
                    while (!recordMetricsCancellationTokenSource.Token.IsCancellationRequested)
                    {
                        recordMetricsCancellationTokenSource.Token.ThrowIfCancellationRequested();

                        metrics.IncrementCounter();
                        metrics.RecordHistogram(10.0f);
                        await Task.Delay(1000).ConfigureAwait(true);
                    }

                }).ConfigureAwait(true);
            }

            await TestBodyCore(customCategoryLogger, appCategoryLogger).ConfigureAwait(false);

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

            recordMetricsCancellationTokenSource.Cancel();

            Console.WriteLine($"{pid} EventPipeTracee {DateTime.UtcNow} Ending remote test process '{input}'");
            return 0;
        }

        // TODO At some point we may want parameters to choose different test bodies.
        private static async Task TestBodyCore(ILogger customCategoryLogger, ILogger appCategoryLogger)
        {
            TaskCompletionSource secondSetScopes = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource firstFinishedLogging = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource secondFinishedLogging = new(TaskCreationOptions.RunContinuationsAsynchronously);

            Task firstTask = Task.Run(async () => {
                using (IDisposable scope = customCategoryLogger.BeginScope(new Dictionary<string, object> {
                    { "IntValue", "5" },
                    { "BoolValue", "true" },
                    { "StringValue", "test" } }.ToList()))
                {
                    // Await for the other task to add its scopes.
                    await secondSetScopes.Task.ConfigureAwait(false);

                    customCategoryLogger.LogInformation("Some warning message with {Arg}", 6);

                    // Signal other task to log
                    firstFinishedLogging.SetResult();

                    // Do not dispose scopes until the other task is done
                    await secondFinishedLogging.Task.ConfigureAwait(false);
                }
            });

            Task secondTask = Task.Run(async () => {
                using (IDisposable scope = customCategoryLogger.BeginScope(new Dictionary<string, object> {
                    { "IntValue", "6" },
                    { "BoolValue", "false" },
                    { "StringValue", "string" } }.ToList()))
                {
                    // Signal that we added our scopes and wait for the other task to log
                    secondSetScopes.SetResult();
                    await firstFinishedLogging.Task.ConfigureAwait(false);
                    customCategoryLogger.LogInformation("Some other message with {Arg}", 7);
                    secondFinishedLogging.SetResult();
                }
            });

            await firstTask.ConfigureAwait(false);
            await secondTask.ConfigureAwait(false);

            //Json data is always converted to strings for ActivityStart events.
            customCategoryLogger.LogWarning(new EventId(7, "AnotherEventId"), "Another message");

            appCategoryLogger.LogInformation("Information message.");
            appCategoryLogger.LogWarning(new EventId(5, "WarningEventId"), "Warning message.");
            appCategoryLogger.LogError("Error message.");
        }
    }
}
