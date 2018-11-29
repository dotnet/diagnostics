using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Diagnostics.Tools.Analyze
{
    internal class LogMessage
    {
        public DateTime Timestamp { get; }
        public string LoggerName { get; }
        public LogLevel Level { get; }
        public EventId EventId { get; }
        public IDictionary<string, string> Arguments { get; }
        public string Message { get; }

        public LogMessage(DateTime timestamp, string loggerName, EventId eventId, LogLevel level, IDictionary<string, string> arguments, string message)
        {
            Timestamp = timestamp;
            LoggerName = loggerName;
            EventId = eventId;
            Level = level;
            Arguments = arguments;
            Message = message;
        }

        internal static LogMessage Load(TraceEvent request)
        {
            var (arguments, message) = LoadArguments((string)request.PayloadByName("ArgumentsJson"));
            var eventIdStr = (string)request.PayloadByName("EventId");
            EventId eventId = default;
            if (int.TryParse(eventIdStr, out var id))
            {
                eventId = new EventId(id);
            }
            else
            {
                eventId = new EventId(0, eventIdStr);
            }

            return new LogMessage(
                request.TimeStamp,
                loggerName: (string)request.PayloadByName("LoggerName"),
                eventId: eventId,
                level: (LogLevel)(int)request.PayloadByName("Level"),
                arguments: arguments,
                message: message);
        }

        private static (IDictionary<string, string>, string) LoadArguments(string json)
        {
            var jobj = JObject.Parse(json);
            var arguments = new Dictionary<string, string>();
            var format = "";
            foreach (var prop in jobj.Properties())
            {
                if (prop.Name.Equals("{OriginalFormat}"))
                {
                    format = prop.Value.ToString();
                }
                else
                {
                    arguments.Add(prop.Name, prop.Value.ToString());
                }
            }

            var message = FormatMessage(format, arguments);
            return (arguments, message);
        }

        private static string FormatMessage(string format, Dictionary<string, string> arguments)
        {
            foreach (var (key, value) in arguments)
            {
                format = format.Replace($"{{{key}}}", value);
            }
            return format;
        }
    }
}
