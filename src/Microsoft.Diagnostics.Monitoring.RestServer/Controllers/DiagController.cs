﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Tracing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Diagnostics.Monitoring.RestServer.Models;
using Microsoft.Diagnostics.Monitoring.RestServer.Validation;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Controllers
{
    [Route("")] // Root
    [ApiController]
    [HostRestriction]
    public class DiagController : ControllerBase
    {
        private const TraceProfile DefaultTraceProfiles = TraceProfile.Cpu | TraceProfile.Http | TraceProfile.Metrics;

        private readonly ILogger<DiagController> _logger;
        private readonly IDiagnosticServices _diagnosticServices;

        public DiagController(ILogger<DiagController> logger, IDiagnosticServices diagnosticServices)
        {
            _logger = logger;
            _diagnosticServices = diagnosticServices;
        }

        [HttpGet("processes")]
        public ActionResult<IEnumerable<ProcessModel>> GetProcesses()
        {
            return this.InvokeService(() =>
            {
                IList<ProcessModel> processes = new List<ProcessModel>();
                foreach (int pid in _diagnosticServices.GetProcesses())
                {
                    processes.Add(new ProcessModel() { Pid = pid });
                }
                return new ActionResult<IEnumerable<ProcessModel>>(processes);
            });
        }

        [HttpGet("dump/{pid?}")]
        public Task<ActionResult> GetDump(int? pid, [FromQuery] DumpType type = DumpType.WithHeap)
        {
            return this.InvokeService(async () =>
            {
                int pidValue = _diagnosticServices.ResolveProcess(pid);
                Stream result = await _diagnosticServices.GetDump(pidValue, type);

                string dumpFileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                    FormattableString.Invariant($"dump_{GetFileNameTimeStampUtcNow()}.dmp") :
                    FormattableString.Invariant($"core_{GetFileNameTimeStampUtcNow()}");

                //Compression is done automatically by the response
                //Chunking is done because the result has no content-length
                return File(result, "application/octet-stream", dumpFileName);
            });
        }

        [HttpGet("gcdump/{pid?}")]
        public Task<ActionResult> GetGcDump(int? pid)
        {
            return this.InvokeService(async () =>
            {
                int pidValue = _diagnosticServices.ResolveProcess(pid);
                Stream result = await _diagnosticServices.GetGcDump(pidValue, this.HttpContext.RequestAborted);
                return File(result, "application/octet-stream", FormattableString.Invariant($"{GetFileNameTimeStampUtcNow()}_{pidValue}.gcdump"));
            });
        }

        [HttpGet("trace/{pid?}")]
        public Task<ActionResult> Trace(
            int? pid,
            [FromQuery]TraceProfile profile = DefaultTraceProfiles,
            [FromQuery][Range(-1, int.MaxValue)] int durationSeconds = 30,
            [FromQuery][Range(1, int.MaxValue)] int metricsIntervalSeconds = 1)
        {
            TimeSpan duration = ConvertSecondsToTimeSpan(durationSeconds);

            return this.InvokeService(async () =>
            {
                var configurations = new List<MonitoringSourceConfiguration>();
                if (profile.HasFlag(TraceProfile.Cpu))
                {
                    configurations.Add(new CpuProfileConfiguration());
                }
                if (profile.HasFlag(TraceProfile.Http))
                {
                    configurations.Add(new HttpRequestSourceConfiguration());
                }
                if (profile.HasFlag(TraceProfile.Logs))
                {
                    configurations.Add(new LoggingSourceConfiguration());
                }
                if (profile.HasFlag(TraceProfile.Metrics))
                {
                    configurations.Add(new MetricSourceConfiguration(metricsIntervalSeconds));
                }

                var aggregateConfiguration = new AggregateSourceConfiguration(configurations.ToArray());

                return await StartTrace(pid, aggregateConfiguration, duration);
            });
        }

        [HttpPost("trace/{pid?}")]
        public Task<ActionResult> TraceCustomConfiguration(
            int? pid,
            [FromBody][Required] EventPipeConfigurationModel configuration,
            [FromQuery][Range(-1, int.MaxValue)] int durationSeconds = 30)
        {
            TimeSpan duration = ConvertSecondsToTimeSpan(durationSeconds);

            return this.InvokeService(async () =>
            {
                var providers = new List<EventPipeProvider>();

                foreach (EventPipeProviderModel providerModel in configuration.Providers)
                {
                    if (!IntegerOrHexStringAttribute.TryParse(providerModel.Keywords, out long keywords, out string parseError))
                    {
                        throw new InvalidOperationException(parseError);
                    }

                    providers.Add(new EventPipeProvider(
                        providerModel.Name,
                        MapEventLevel(providerModel.EventLevel),
                        keywords,
                        providerModel.Arguments
                        ));
                }

                var traceConfiguration = new EventPipeProviderSourceConfiguration(
                    providers: providers.ToArray(),
                    requestRundown: configuration.RequestRundown,
                    bufferSizeInMB: configuration.BufferSizeInMB);

                return await StartTrace(pid, traceConfiguration, duration);
            });
        }

        [HttpGet("logs/{pid?}")]
        public ActionResult Logs(int? pid, [FromQuery][Range(-1, int.MaxValue)] int durationSeconds = 30)
        {
            TimeSpan duration = ConvertSecondsToTimeSpan(durationSeconds);
            return this.InvokeService(() =>
            {
                int pidValue = _diagnosticServices.ResolveProcess(pid);
                return new OutputStreamResult(async (outputStream, token) =>
                {
                    await _diagnosticServices.StartLogs(outputStream, pidValue, duration, token);
                }, "application/x-ndjson", FormattableString.Invariant($"{Guid.NewGuid()}.txt"));
            });
        }

        private async Task<StreamWithCleanupResult> StartTrace(int? pid, MonitoringSourceConfiguration configuration, TimeSpan duration)
        {
            int pidValue = _diagnosticServices.ResolveProcess(pid);
            IStreamWithCleanup result = await _diagnosticServices.StartTrace(pidValue, configuration, duration, this.HttpContext.RequestAborted);
            return new StreamWithCleanupResult(result, "application/octet-stream", FormattableString.Invariant($"{Guid.NewGuid()}.nettrace"));
        }

        private static EventLevel MapEventLevel(EventPipeProviderEventLevel eventLevel)
        {
            switch (eventLevel)
            {
                case EventPipeProviderEventLevel.Critical:
                    return EventLevel.Critical;
                case EventPipeProviderEventLevel.Error:
                    return EventLevel.Error;
                case EventPipeProviderEventLevel.Informational:
                    return EventLevel.Informational;
                case EventPipeProviderEventLevel.LogAlways:
                    return EventLevel.LogAlways;
                case EventPipeProviderEventLevel.Verbose:
                    return EventLevel.Verbose;
                case EventPipeProviderEventLevel.Warning:
                    return EventLevel.Warning;
                default:
                    throw new ArgumentException("Unexpected event level", nameof(eventLevel));
            }
        }

        private static TimeSpan ConvertSecondsToTimeSpan(int durationSeconds)
        {
            return durationSeconds < 0 ?
                Timeout.InfiniteTimeSpan :
                TimeSpan.FromSeconds(durationSeconds);
        }

        private static string GetFileNameTimeStampUtcNow()
        {
            return DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        }
    }
}
