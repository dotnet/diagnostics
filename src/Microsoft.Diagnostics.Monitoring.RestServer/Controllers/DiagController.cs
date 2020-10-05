// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Diagnostics.Monitoring.EventPipe;
using Microsoft.Diagnostics.Monitoring.RestServer.Models;
using Microsoft.Diagnostics.Monitoring.RestServer.Validation;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Controllers
{
    [Route("")] // Root
    [ApiController]
    [HostRestriction]
    public class DiagController : ControllerBase
    {
        private const TraceProfile DefaultTraceProfiles = TraceProfile.Cpu | TraceProfile.Http | TraceProfile.Metrics;
        private const string ContentTypeNdJson = "application/x-ndjson";
        private const string ContentTypeJson = "application/json";
        private const string ContentTypeEventStream = "text/event-stream";
        private static readonly MediaTypeHeaderValue NdJsonHeader = new MediaTypeHeaderValue(ContentTypeNdJson);
        private static readonly MediaTypeHeaderValue EventStreamHeader = new MediaTypeHeaderValue(ContentTypeEventStream);

        private readonly ILogger<DiagController> _logger;
        private readonly IDiagnosticServices _diagnosticServices;

        public DiagController(ILogger<DiagController> logger, IDiagnosticServices diagnosticServices)
        {
            _logger = logger;
            _diagnosticServices = diagnosticServices;
        }

        [HttpGet("processes")]
        public Task<ActionResult<IEnumerable<ProcessIdentifierModel>>> GetProcesses()
        {
            return this.InvokeService(async () =>
            {
                IList<ProcessIdentifierModel> processesIdentifiers = new List<ProcessIdentifierModel>();
                foreach (IProcessInfo p in await _diagnosticServices.GetProcessesAsync(HttpContext.RequestAborted))
                {
                    processesIdentifiers.Add(ProcessIdentifierModel.FromProcessInfo(p));
                }
                return new ActionResult<IEnumerable<ProcessIdentifierModel>>(processesIdentifiers);
            });
        }

        [HttpGet("processes/{processFilter}")]
        public Task<ActionResult<ProcessModel>> GetProcess(
            ProcessFilter processFilter)
        {
            return this.InvokeService<ProcessModel>(async () =>
            {
                IProcessInfo processInfo = await _diagnosticServices.GetProcessAsync(
                    processFilter,
                    HttpContext.RequestAborted);

                return ProcessModel.FromProcessInfo(processInfo);
            });
        }

        [HttpGet("processes/{processFilter}/env")]
        public Task<ActionResult<Dictionary<string, string>>> GetProcessEnvironment(
            ProcessFilter processFilter)
        {
            return this.InvokeService<Dictionary<string, string>>(async () =>
            {
                IProcessInfo processInfo = await _diagnosticServices.GetProcessAsync(
                    processFilter,
                    HttpContext.RequestAborted);

                try
                {
                    return processInfo.Client.GetProcessEnvironment();
                }
                catch (ServerErrorException)
                {
                    throw new InvalidOperationException("Unable to get process environment.");
                }
            });
        }

        [HttpGet("dump/{processFilter?}")]
        public Task<ActionResult> GetDump(
            ProcessFilter? processFilter,
            [FromQuery] DumpType type = DumpType.WithHeap)
        {
            return this.InvokeService(async () =>
            {
                IProcessInfo processInfo = await _diagnosticServices.GetProcessAsync(processFilter, HttpContext.RequestAborted);
                Stream result = await _diagnosticServices.GetDump(processInfo, type, HttpContext.RequestAborted);

                string dumpFileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                    FormattableString.Invariant($"dump_{GetFileNameTimeStampUtcNow()}.dmp") :
                    FormattableString.Invariant($"core_{GetFileNameTimeStampUtcNow()}");

                //Compression is done automatically by the response
                //Chunking is done because the result has no content-length
                return File(result, "application/octet-stream", dumpFileName);
            });
        }

        [HttpGet("gcdump/{processFilter?}")]
        public Task<ActionResult> GetGcDump(
            ProcessFilter? processFilter)
        {
            return this.InvokeService(async () =>
            {
                IProcessInfo processInfo = await _diagnosticServices.GetProcessAsync(processFilter, HttpContext.RequestAborted);

                var graph = new Graphs.MemoryGraph(50_000);

                EventGCPipelineSettings settings = new EventGCPipelineSettings
                {
                    Duration = Timeout.InfiniteTimeSpan,
                };
                await using var pipeline = new EventGCDumpPipeline(processInfo.Client, settings, graph);
                await pipeline.RunAsync(HttpContext.RequestAborted);
                var dumper = new GCHeapDump(graph);
                dumper.CreationTool = "dotnet-monitor";

                //We can't use FastSerialization directly against the Response stream because
                //the response stream size is not known.
                var stream = new MemoryStream();
                var serializer = new FastSerialization.Serializer(stream, dumper, leaveOpen: true);
                serializer.Close();

                stream.Position = 0;

                return File(stream, "application/octet-stream", FormattableString.Invariant($"{GetFileNameTimeStampUtcNow()}_{processInfo.ProcessId}.gcdump"));
            });
        }

        [HttpGet("trace/{processFilter?}")]
        public Task<ActionResult> Trace(
            ProcessFilter? processFilter,
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
                    configurations.Add(new MetricSourceConfiguration(metricsIntervalSeconds, Enumerable.Empty<string>()));
                }

                var aggregateConfiguration = new AggregateSourceConfiguration(configurations.ToArray());

                return await StartTrace(processFilter, aggregateConfiguration, duration);
            });
        }

        [HttpPost("trace/{processFilter?}")]
        public Task<ActionResult> TraceCustomConfiguration(
            ProcessFilter? processFilter,
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
                        providerModel.EventLevel,
                        keywords,
                        providerModel.Arguments
                        ));
                }

                var traceConfiguration = new EventPipeProviderSourceConfiguration(
                    providers: providers.ToArray(),
                    requestRundown: configuration.RequestRundown,
                    bufferSizeInMB: configuration.BufferSizeInMB);

                return await StartTrace(processFilter, traceConfiguration, duration);
            });
        }

        [HttpGet("logs/{processFilter?}")]
        [Produces(ContentTypeEventStream, ContentTypeNdJson, ContentTypeJson)]
        public Task<ActionResult> Logs(
            ProcessFilter? processFilter,
            [FromQuery][Range(-1, int.MaxValue)] int durationSeconds = 30,
            [FromQuery] LogLevel level = LogLevel.Debug)
        {
            TimeSpan duration = ConvertSecondsToTimeSpan(durationSeconds);
            return this.InvokeService(async () =>
            {
                IProcessInfo processInfo = await _diagnosticServices.GetProcessAsync(processFilter, HttpContext.RequestAborted);

                LogFormat format = ComputeLogFormat(Request.GetTypedHeaders().Accept);
                if (format == LogFormat.None)
                {
                    return this.NotAcceptable();
                }

                string contentType = (format == LogFormat.EventStream) ? ContentTypeEventStream : ContentTypeNdJson;
                string downloadName = (format == LogFormat.EventStream) ? null : FormattableString.Invariant($"{GetFileNameTimeStampUtcNow()}_{processInfo.ProcessId}.txt");

                return new OutputStreamResult(async (outputStream, token) =>
                {
                    using var loggerFactory = new LoggerFactory();

                    loggerFactory.AddProvider(new StreamingLoggerProvider(outputStream, format, level));

                    var settings = new EventLogsPipelineSettings
                    {
                        Duration = duration,
                        LogLevel = level,
                    };
                    await using EventLogsPipeline pipeline = new EventLogsPipeline(processInfo.Client, settings, loggerFactory);
                    await pipeline.RunAsync(token);

                }, contentType, downloadName);
            });
        }

        private async Task<ActionResult> StartTrace(
            ProcessFilter? processFilter,
            MonitoringSourceConfiguration configuration,
            TimeSpan duration)
        {
            IProcessInfo processInfo = await _diagnosticServices.GetProcessAsync(processFilter, HttpContext.RequestAborted);

            return new OutputStreamResult(async (outputStream, token) =>
            {
                Func<Stream, CancellationToken, Task> streamAvailable = async (Stream eventStream, CancellationToken token) =>
                {
                    //Buffer size matches FileStreamResult
                    //CONSIDER Should we allow client to change the buffer size?
                    await eventStream.CopyToAsync(outputStream, 0x10000, token);
                };

                await using EventTracePipeline pipeProcessor = new EventTracePipeline(processInfo.Client, new EventTracePipelineSettings
                {
                    Configuration = configuration,
                    Duration = duration,
                }, streamAvailable);

                await pipeProcessor.RunAsync(token);
            }, "application/octet-stream", FormattableString.Invariant($"{GetFileNameTimeStampUtcNow()}_{processInfo.ProcessId}.nettrace"));
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

        private static LogFormat ComputeLogFormat(IList<MediaTypeHeaderValue> acceptedHeaders)
        {
            if (acceptedHeaders == null)
            {
                return LogFormat.None;
            }

            if (acceptedHeaders.Contains(EventStreamHeader))
            {
                return LogFormat.EventStream;
            }
            if (acceptedHeaders.Contains(NdJsonHeader))
            {
                return LogFormat.Json;
            }
            if (acceptedHeaders.Any(h => EventStreamHeader.IsSubsetOf(h)))
            {
                return LogFormat.EventStream;
            }
            if (acceptedHeaders.Any(h => NdJsonHeader.IsSubsetOf(h)))
            {
                return LogFormat.Json;
            }
            return LogFormat.None;
        }
    }
}
