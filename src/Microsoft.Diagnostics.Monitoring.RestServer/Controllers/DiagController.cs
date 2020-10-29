// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using FastSerialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Diagnostics.Monitoring.EventPipe;
using Microsoft.Diagnostics.Monitoring.RestServer.Models;
using Microsoft.Diagnostics.Monitoring.RestServer.Validation;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Controllers
{
    [Route("")] // Root
    [ApiController]
    [HostRestriction]
    public class DiagController : ControllerBase
    {
        private const TraceProfile DefaultTraceProfiles = TraceProfile.Cpu | TraceProfile.Http | TraceProfile.Metrics;
        private static readonly MediaTypeHeaderValue NdJsonHeader = new MediaTypeHeaderValue(ContentTypes.ApplicationNdJson);
        private static readonly MediaTypeHeaderValue EventStreamHeader = new MediaTypeHeaderValue(ContentTypes.TextEventStream);

        private readonly ILogger<DiagController> _logger;
        private readonly IDiagnosticServices _diagnosticServices;

        public DiagController(ILogger<DiagController> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _diagnosticServices = serviceProvider.GetRequiredService<IDiagnosticServices>();
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

                var client = new DiagnosticsClient(processInfo.EndpointInfo.Endpoint);

                try
                {
                    return client.GetProcessEnvironment();
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
            [FromQuery] DumpType type = DumpType.WithHeap,
            [FromQuery] string egressEndpoint = null)
        {
            return this.InvokeService(async () =>
            {
                IProcessInfo processInfo = await _diagnosticServices.GetProcessAsync(processFilter, HttpContext.RequestAborted);

                string dumpFileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                    FormattableString.Invariant($"dump_{GetFileNameTimeStampUtcNow()}.dmp") :
                    FormattableString.Invariant($"core_{GetFileNameTimeStampUtcNow()}");

                if (string.IsNullOrEmpty(egressEndpoint))
                {
                    Stream dumpStream = await _diagnosticServices.GetDump(processInfo, type, HttpContext.RequestAborted);

                    //Compression is done automatically by the response
                    //Chunking is done because the result has no content-length
                    return File(dumpStream, ContentTypes.ApplicationOctectStream, dumpFileName);
                }
                else
                {
                    return new EgressStreamResult(
                        token => _diagnosticServices.GetDump(processInfo, type, token),
                        egressEndpoint,
                        dumpFileName,
                        processInfo.EndpointInfo,
                        ContentTypes.ApplicationOctectStream);
                }
            });
        }

        [HttpGet("gcdump/{processFilter?}")]
        public Task<ActionResult> GetGcDump(
            ProcessFilter? processFilter,
            [FromQuery] string egressEndpoint = null)
        {
            return this.InvokeService(async () =>
            {
                IProcessInfo processInfo = await _diagnosticServices.GetProcessAsync(processFilter, HttpContext.RequestAborted);

                string fileName = FormattableString.Invariant($"{GetFileNameTimeStampUtcNow()}_{processInfo.EndpointInfo.ProcessId}.gcdump");

                Func<CancellationToken, Task<IFastSerializable>> action = async (token) => {
                    var graph = new Graphs.MemoryGraph(50_000);

                    EventGCPipelineSettings settings = new EventGCPipelineSettings
                    {
                        Duration = Timeout.InfiniteTimeSpan,
                    };

                    var client = new DiagnosticsClient(processInfo.EndpointInfo.Endpoint);

                    await using var pipeline = new EventGCDumpPipeline(client, settings, graph);
                    await pipeline.RunAsync(token);

                    return new GCHeapDump(graph)
                    {
                        CreationTool = "dotnet-monitor"
                    };
                };

                if (string.IsNullOrEmpty(egressEndpoint))
                {
                    return new OutputStreamResult(
                        ConvertFastSerializeAction(action),
                        ContentTypes.ApplicationOctectStream,
                        fileName);
                }
                else
                {
                    return new EgressStreamResult(
                        ConvertFastSerializeAction(action),
                        egressEndpoint,
                        fileName,
                        processInfo.EndpointInfo,
                        ContentTypes.ApplicationOctectStream);
                }
            });
        }

        [HttpGet("trace/{processFilter?}")]
        public Task<ActionResult> Trace(
            ProcessFilter? processFilter,
            [FromQuery]TraceProfile profile = DefaultTraceProfiles,
            [FromQuery][Range(-1, int.MaxValue)] int durationSeconds = 30,
            [FromQuery][Range(1, int.MaxValue)] int metricsIntervalSeconds = 1,
            [FromQuery] string egressEndpoint = null)
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

                return await StartTrace(processFilter, aggregateConfiguration, duration, egressEndpoint);
            });
        }

        [HttpPost("trace/{processFilter?}")]
        public Task<ActionResult> TraceCustomConfiguration(
            ProcessFilter? processFilter,
            [FromBody][Required] EventPipeConfigurationModel configuration,
            [FromQuery][Range(-1, int.MaxValue)] int durationSeconds = 30,
            [FromQuery] string egressEndpoint = null)
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

                return await StartTrace(processFilter, traceConfiguration, duration, egressEndpoint);
            });
        }

        [HttpGet("logs/{processFilter?}")]
        [Produces(ContentTypes.TextEventStream, ContentTypes.ApplicationNdJson, ContentTypes.ApplicationJson)]
        public Task<ActionResult> Logs(
            ProcessFilter? processFilter,
            [FromQuery][Range(-1, int.MaxValue)] int durationSeconds = 30,
            [FromQuery] LogLevel level = LogLevel.Debug,
            [FromQuery] string egressEndpoint = null)
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

                string fileName = FormattableString.Invariant($"{GetFileNameTimeStampUtcNow()}_{processInfo.EndpointInfo.ProcessId}.txt");
                string contentType = format == LogFormat.EventStream ? ContentTypes.TextEventStream : ContentTypes.ApplicationNdJson;

                Func<Stream, CancellationToken, Task> action = async (outputStream, token) =>
                {
                    using var loggerFactory = new LoggerFactory();

                    loggerFactory.AddProvider(new StreamingLoggerProvider(outputStream, format, level));

                    var settings = new EventLogsPipelineSettings
                    {
                        Duration = duration,
                        LogLevel = level,
                    };

                    var client = new DiagnosticsClient(processInfo.EndpointInfo.Endpoint);

                    await using EventLogsPipeline pipeline = new EventLogsPipeline(client, settings, loggerFactory);
                    await pipeline.RunAsync(token);
                };

                if (!string.IsNullOrEmpty(egressEndpoint))
                {
                    return new EgressStreamResult(
                        action,
                        egressEndpoint,
                        fileName,
                        processInfo.EndpointInfo,
                        contentType);
                }
                else
                {
                    string downloadName = format == LogFormat.EventStream ? null : fileName;
                    return new OutputStreamResult(
                        action,
                        contentType,
                        downloadName);
                }
            });
        }

        private async Task<ActionResult> StartTrace(
            ProcessFilter? processFilter,
            MonitoringSourceConfiguration configuration,
            TimeSpan duration,
            string egressEndpoint)
        {
            IProcessInfo processInfo = await _diagnosticServices.GetProcessAsync(processFilter, HttpContext.RequestAborted);

            string fileName = FormattableString.Invariant($"{GetFileNameTimeStampUtcNow()}_{processInfo.EndpointInfo.ProcessId}.nettrace");

            Func<Stream, CancellationToken, Task> action = async (outputStream, token) =>
            {
                Func<Stream, CancellationToken, Task> streamAvailable = async (Stream eventStream, CancellationToken token) =>
                {
                    //Buffer size matches FileStreamResult
                    //CONSIDER Should we allow client to change the buffer size?
                    await eventStream.CopyToAsync(outputStream, 0x10000, token);
                };

                var client = new DiagnosticsClient(processInfo.EndpointInfo.Endpoint);

                await using EventTracePipeline pipeProcessor = new EventTracePipeline(client, new EventTracePipelineSettings
                {
                    Configuration = configuration,
                    Duration = duration,
                }, streamAvailable);

                await pipeProcessor.RunAsync(token);
            };

            if (string.IsNullOrEmpty(egressEndpoint))
            {
                return new OutputStreamResult(
                    action,
                    ContentTypes.ApplicationOctectStream,
                    fileName);
            }
            else
            {
                return new EgressStreamResult(
                    action,
                    egressEndpoint,
                    fileName,
                    processInfo.EndpointInfo,
                    ContentTypes.ApplicationOctectStream);
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

        private static Func<Stream, CancellationToken, Task> ConvertFastSerializeAction(Func<CancellationToken, Task<IFastSerializable>> action)
        {
            return async (stream, token) =>
            {
                IFastSerializable fastSerializable = await action(token);

                // FastSerialization requests the length of the stream before serializing to the stream.
                // If the stream is a response stream, requesting the length or setting the position is
                // not supported. Create an intermediate buffer if testing the stream fails.
                bool useIntermediateStream = false;
                try
                {
                    _ = stream.Length;
                }
                catch (NotSupportedException)
                {
                    useIntermediateStream = true;
                }

                if (useIntermediateStream)
                {
                    using var intermediateStream = new MemoryStream();

                    var serializer = new Serializer(intermediateStream, fastSerializable, leaveOpen: true);
                    serializer.Close();

                    intermediateStream.Position = 0;

                    await intermediateStream.CopyToAsync(stream, 0x10000, token);
                }
                else
                {
                    var serializer = new Serializer(stream, fastSerializable, leaveOpen: true);
                    serializer.Close();
                }
            };
        }
    }
}
