using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EventPipeTracee
{
    class Program
    {
        static void Main(string[] args)
        {
            TestBody(args[0]);
        }

        private static void TestBody(string loggerCategory)
        {
            Console.WriteLine("Starting remote test process");

            ServiceCollection serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging(builder =>
            {
                builder.AddEventSourceLogger();
            });

            using var loggerFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(loggerCategory);

            Console.WriteLine($"{DateTime.UtcNow} Awaiting start");
            if (Console.Read() == -1)
            {
                throw new InvalidOperationException("Unable to receive start signal");
            }

            Console.WriteLine($"{DateTime.UtcNow} Starting test body");
            TestBodyCore(logger);

            Console.WriteLine($"{DateTime.UtcNow} Awaiting end");
            if (Console.Read() == -1)
            {
                throw new InvalidOperationException("Unable to receive end signal");
            }


            Console.WriteLine($"{DateTime.UtcNow} Ending remote test process");
        }

        //TODO At some point we may want parameters to choose different test bodies.
        private static void TestBodyCore(ILogger logger)
        {
            //Json data is always converted to strings for ActivityStart events.
            using (var scope = logger.BeginScope(new Dictionary<string, object> {
                    { "IntValue", "5" },
                    { "BoolValue", "true" },
                    { "StringValue", "test" } }.ToList()))
            {
                logger.LogWarning("Some warning message with {arg}", 6);
            }

            logger.LogWarning("Another message");
        }
    }
}
