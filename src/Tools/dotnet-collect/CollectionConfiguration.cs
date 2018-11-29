using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Collect
{
    public class CollectionConfiguration
    {
        public int? ProcessId { get; set; }
        public string OutputPath { get; set; }
        public int? CircularMB { get; set; }
        public IList<EventSpec> Providers { get; set; } = new List<EventSpec>();
        public IList<LoggerSpec> Loggers { get; set; } = new List<LoggerSpec>();

        internal string ToConfigString()
        {
            var builder = new StringBuilder();
            if (ProcessId != null)
            {
                builder.AppendLine($"ProcessId={ProcessId.Value}");
            }
            if (!string.IsNullOrEmpty(OutputPath))
            {
                builder.AppendLine($"OutputPath={OutputPath}");
            }
            if (CircularMB != null)
            {
                builder.AppendLine($"CircularMB={CircularMB}");
            }
            if (Providers != null && Providers.Count > 0)
            {
                builder.AppendLine($"Providers={SerializeProviders(Enumerable.Concat(Providers, GenerateLoggerSpec(Loggers)))}");
            }
            return builder.ToString();
        }

        public void AddProfile(CollectionProfile profile)
        {
            foreach (var provider in profile.EventSpecs)
            {
                Providers.Add(provider);
            }

            foreach (var logger in profile.LoggerSpecs)
            {
                Loggers.Add(logger);
            }
        }

        private string SerializeProviders(IEnumerable<EventSpec> providers) => string.Join(",", providers.Select(s => s.ToConfigString()));

        private IEnumerable<EventSpec> GenerateLoggerSpec(IList<LoggerSpec> loggers)
        {
            if (loggers.Count > 0)
            {
                var filterSpec = new StringBuilder();
                foreach (var logger in loggers)
                {
                    if (string.IsNullOrEmpty(logger.Level))
                    {
                        filterSpec.Append($"{logger.Prefix}");
                    }
                    else
                    {
                        filterSpec.Append($"{logger.Prefix}:{logger.Level}");
                    }
                    filterSpec.Append(";");
                }

                // Remove trailing ';'
                filterSpec.Length -= 1;

                yield return new EventSpec(
                    provider: "Microsoft-Extensions-Logging",
                    keywords: 0x04 | 0x08, // FormattedMessage | JsonMessage (source: https://github.com/aspnet/Extensions/blob/aa7fa91cfc8f6ff078b020a428bcad71ae7a32ab/src/Logging/Logging.EventSource/src/LoggingEventSource.cs#L95)
                    level: EventLevel.LogAlways,
                    parameters: new Dictionary<string, string>() {
                    { "FilterSpecs", filterSpec.ToString() }
                    });
            }
        }
    }
}
