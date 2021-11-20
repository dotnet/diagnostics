// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventPipeTracee
{
    internal class Program
    {
        private const string AppLoggerCategoryName = "AppLoggerCategory";

        private static void Main(string[] args)
        {
            bool spinWait10 = args.Length > 1 && "SpinWait10".Equals(args[1], StringComparison.Ordinal);
            TestBody(args[0], spinWait10);
        }

        private static void TestBody(string loggerCategory, bool spinWait10)
        {
            Console.Error.WriteLine("Starting remote test process");
            Console.Error.Flush();

            ServiceCollection serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging(builder => {
                builder.AddEventSourceLogger();
                // Set application defined levels
                builder.AddFilter(null, LogLevel.Error); // Default
                builder.AddFilter(AppLoggerCategoryName, LogLevel.Warning);
            });

            using ILoggerFactory loggerFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();
            ILogger customCategoryLogger = loggerFactory.CreateLogger(loggerCategory);
            ILogger appCategoryLogger = loggerFactory.CreateLogger(AppLoggerCategoryName);

            Console.Error.WriteLine($"{DateTime.UtcNow} Awaiting start");
            Console.Error.Flush();
            if (Console.Read() == -1)
            {
                throw new InvalidOperationException("Unable to receive start signal");
            }

            Console.Error.WriteLine($"{DateTime.UtcNow} Starting test body");
            Console.Error.Flush();
            TestBodyCore(customCategoryLogger, appCategoryLogger);

            //Signal end of test data
            Console.WriteLine("1");

            if (spinWait10)
            {
                DateTime targetDateTime = DateTime.UtcNow.AddSeconds(10);

                long acc = 0;
                while (DateTime.UtcNow < targetDateTime)
                {
                    acc++;
                    if (acc % 1_000_000 == 0)
                    {
                        Console.Error.WriteLine("Spin waiting...");
                    }
                }
            }

            Console.Error.WriteLine($"{DateTime.UtcNow} Awaiting end");
            Console.Error.Flush();
            if (Console.Read() == -1)
            {
                throw new InvalidOperationException("Unable to receive end signal");
            }

            Console.Error.WriteLine($"{DateTime.UtcNow} Ending remote test process");
        }

        //TODO At some point we may want parameters to choose different test bodies.
        private static void TestBodyCore(ILogger customCategoryLogger, ILogger appCategoryLogger)
        {
            //Json data is always converted to strings for ActivityStart events.
            using (IDisposable scope = customCategoryLogger.BeginScope(new Dictionary<string, object> {
                    { "IntValue", "5" },
                    { "BoolValue", "true" },
                    { "StringValue", "test" } }.ToList()))
            {
                customCategoryLogger.LogInformation("Some warning message with {arg}", 6);
            }

            customCategoryLogger.LogWarning(new EventId(7, "AnotherEventId"), "Another message");

            appCategoryLogger.LogInformation("Information message.");
            appCategoryLogger.LogWarning(new EventId(5, "WarningEventId"), "Warning message.");
            appCategoryLogger.LogError("Error message.");
        }
    }
}
