using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace Microsoft.Diagnostics.Tools.Collect
{
    public class EtwCollector : EventCollector
    {
        private readonly CollectionConfiguration _config;
        private TraceEventSession _session;

        public EtwCollector(CollectionConfiguration config)
        {
            _config = config;
        }

        public override Task StartCollectingAsync()
        {
            // TODO: Allow a file name to be provided
            var outputFile = _config.ProcessId == null ?
                Path.Combine(_config.OutputPath, "dotnet-collect.etl") :
                Path.Combine(_config.OutputPath, $"dotnet-collect.{_config.ProcessId.Value}.etl");
            if (File.Exists(outputFile))
            {
                throw new InvalidOperationException($"Target file already exists: {outputFile}");
            }
            _session = new TraceEventSession("dotnet-collect", outputFile);

            if (_config.CircularMB is int circularMb)
            {
                _session.CircularBufferMB = circularMb;
            }

            var options = new TraceEventProviderOptions();
            if (_config.ProcessId is int pid)
            {
                options.ProcessIDFilter = new List<int>() { pid };
            }

            // Enable the providers requested
            foreach (var provider in _config.Providers)
            {
                _session.EnableProvider(provider.Provider, ConvertLevel(provider.Level), provider.Keywords, options);
            }

            return Task.CompletedTask;
        }

        private TraceEventLevel ConvertLevel(EventLevel level)
        {
            switch (level)
            {
                case EventLevel.Critical: return TraceEventLevel.Critical;
                case EventLevel.Error: return TraceEventLevel.Error;
                case EventLevel.Informational: return TraceEventLevel.Informational;
                case EventLevel.LogAlways: return TraceEventLevel.Always;
                case EventLevel.Verbose: return TraceEventLevel.Verbose;
                case EventLevel.Warning: return TraceEventLevel.Warning;
                default:
                    throw new InvalidOperationException($"Unknown EventLevel: {level}");
            }
        }

        public override Task StopCollectingAsync()
        {
            _session.Dispose();
            return Task.CompletedTask;
        }
    }
}
