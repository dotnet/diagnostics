// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using FastSerialization;
using Microsoft.AspNetCore.Authorization;
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
    [Authorize(Policy = AuthConstants.PolicyName)]
    public class DiagController : ControllerBase
    {
        private const string ArtifactType_Dump = "dump";
        private const string ArtifactType_GCDump = "gcdump";
        private const string ArtifactType_Logs = "logs";
        private const string ArtifactType_Trace = "trace";

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
                _logger.WrittenToHttpStream();
                return new ActionResult<IEnumerable<ProcessIdentifierModel>>(processesIdentifiers);
            }, _logger);
        }

        [HttpGet("processes/{processFilter}")]
        public Task<ActionResult<ProcessModel>> GetProcess(
            ProcessFilter processFilter)
        {
            return InvokeForProcess<ProcessModel>(processInfo =>
            {
                ProcessModel processModel = ProcessModel.FromProcessInfo(processInfo);

                _logger.WrittenToHttpStream();

                return processModel;
            },
            processFilter);
        }

        [HttpGet("processes/{processFilter}/env")]
        public Task<ActionResult<Dictionary<string, string>>> GetProcessEnvironment(
            ProcessFilter processFilter)
        {
            return InvokeForProcess<Dictionary<string, string>>(processInfo =>
            {
                var client = new DiagnosticsClient(processInfo.EndpointInfo.Endpoint);

                try
                {
                    Dictionary<string, string> environment = client.GetProcessEnvironment();

                    _logger.WrittenToHttpStream();

                    return environment;
                }
                catch (ServerErrorException)
                {
                    throw new InvalidOperationException("Unable to get process environment.");
                }
            },
            processFilter);
        }

        [HttpGet("dump/{processFilter?}")]
        public Task<ActionResult> GetDump(
            ProcessFilter? processFilter,
            [FromQuery] DumpType type = DumpType.WithHeap,
            [FromQuery] string egressProvider = null)
        {
            return InvokeForProcess(async processInfo =>
            {
                string dumpFileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                    FormattableString.Invariant($"dump_{GetFileNameTimeStampUtcNow()}.dmp") :
                    FormattableString.Invariant($"core_{GetFileNameTimeStampUtcNow()}");

                if (string.IsNullOrEmpty(egressProvider))
                {
                    Stream dumpStream = await _diagnosticServices.GetDump(processInfo, type, HttpContext.RequestAborted);

                    _logger.WrittenToHttpStream();
                    //Compression is done automatically by the response
                    //Chunking is done because the result has no content-length
                    return File(dumpStream, ContentTypes.ApplicationOctectStream, dumpFileName);
                }
                else
                {
                    KeyValueLogScope scope = new KeyValueLogScope();
                    scope.AddArtifactType(ArtifactType_Dump);
                    scope.AddEndpointInfo(processInfo.EndpointInfo);

                    return new EgressStreamResult(
                        token => _diagnosticServices.GetDump(processInfo, type, token),
                        egressProvider,
                        dumpFileName,
                        processInfo.EndpointInfo,
                        ContentTypes.ApplicationOctectStream,
                        scope);
                }
            }, processFilter, ArtifactType_Dump);
        }

        [HttpGet("gcdump/{processFilter?}")]
        public Task<ActionResult> GetGcDump(
            ProcessFilter? processFilter,
            [FromQuery] string egressProvider = null)
        {
            return InvokeForProcess(processInfo =>
            {
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

                return Result(
                    ArtifactType_GCDump,
                    egressProvider,
                    ConvertFastSerializeAction(action),
                    fileName,
                    ContentTypes.ApplicationOctectStream,
                    processInfo.EndpointInfo);
            }, processFilter, ArtifactType_GCDump);
        }

        [HttpGet("trace/{processFilter?}")]
        public Task<ActionResult> Trace(
            ProcessFilter? processFilter,
            [FromQuery]TraceProfile profile = DefaultTraceProfiles,
            [FromQuery][Range(-1, int.MaxValue)] int durationSeconds = 30,
            [FromQuery][Range(1, int.MaxValue)] int metricsIntervalSeconds = 1,
            [FromQuery] string egressProvider = null)
        {
            return InvokeForProcess(processInfo =>
            {
                TimeSpan duration = ConvertSecondsToTimeSpan(durationSeconds);

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

                return StartTrace(processInfo, aggregateConfiguration, duration, egressProvider);
            }, processFilter, ArtifactType_Trace);
        }

        [HttpPost("trace/{processFilter?}")]
        public Task<ActionResult> TraceCustomConfiguration(
            ProcessFilter? processFilter,
            [FromBody][Required] EventPipeConfigurationModel configuration,
            [FromQuery][Range(-1, int.MaxValue)] int durationSeconds = 30,
            [FromQuery] string egressProvider = null)
        {
            return InvokeForProcess(processInfo =>
            {
                TimeSpan duration = ConvertSecondsToTimeSpan(durationSeconds);

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

                return StartTrace(processInfo, traceConfiguration, duration, egressProvider);
            }, processFilter, ArtifactType_Trace);
        }

        [HttpGet("logs/{processFilter?}")]
        [Produces(ContentTypes.TextEventStream, ContentTypes.ApplicationNdJson, ContentTypes.ApplicationJson)]
        public Task<ActionResult> Logs(
            ProcessFilter? processFilter,
            [FromQuery][Range(-1, int.MaxValue)] int durationSeconds = 30,
            [FromQuery] LogLevel level = LogLevel.Debug,
            [FromQuery] string egressProvider = null)
        {
            return InvokeForProcess(processInfo =>
            {
                TimeSpan duration = ConvertSecondsToTimeSpan(durationSeconds);

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

                return Result(
                    ArtifactType_Logs,
                    egressProvider,
                    action,
                    fileName,
                    contentType,
                    processInfo.EndpointInfo,
                    format != LogFormat.EventStream);
            }, processFilter, ArtifactType_Logs);
        }

        private ActionResult StartTrace(
            IProcessInfo processInfo,
            MonitoringSourceConfiguration configuration,
            TimeSpan duration,
            string egressProvider)
        {
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

            return Result(
                ArtifactType_Trace,
                egressProvider,
                action,
                fileName,
                ContentTypes.ApplicationOctectStream,
                processInfo.EndpointInfo);
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

        private ActionResult Result(
            string artifactType,
            string providerName,
            Func<Stream, CancellationToken, Task> action,
            string fileName,
            string contentType,
            IEndpointInfo endpointInfo,
            bool asAttachment = true)
        {
            KeyValueLogScope scope = new KeyValueLogScope();
            scope.AddArtifactType(artifactType);
            scope.AddEndpointInfo(endpointInfo);

            if (string.IsNullOrEmpty(providerName))
            {
                return new OutputStreamResult(
                    action,
                    contentType,
                    asAttachment ? fileName : null,
                    scope);
            }
            else
            {
                return new EgressStreamResult(
                    action,
                    providerName,
                    fileName,
                    endpointInfo,
                    contentType,
                    scope);
            }
        }

        private static Func<Stream, CancellationToken, Task> ConvertFastSerializeAction(Func<CancellationToken, Task<IFastSerializable>> action)
        {
            return async (stream, token) =>
            {
                IFastSerializable fastSerializable = await action(token);

                // FastSerialization requests the length of the stream before serializing to the stream.
                // If the stream is a response stream, requesting the length or setting the position is
                // not supported. Create an intermediate buffer if testing the stream fails.
                // This can use a huge amount of memory if the IFastSerializable is very large.
                // CONSIDER: Update FastSerialization to not get the length or attempt to reset the position.
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

        private Task<ActionResult> InvokeForProcess(Func<IProcessInfo, ActionResult> func, ProcessFilter? filter, string artifactType = null)
        {
            Func<IProcessInfo, Task<ActionResult>> asyncFunc =
                processInfo => Task.FromResult(func(processInfo));

            return InvokeForProcess(asyncFunc, filter, artifactType);
        }

        private async Task<ActionResult> InvokeForProcess(Func<IProcessInfo, Task<ActionResult>> func, ProcessFilter? filter, string artifactType)
        {
            ActionResult<object> result = await InvokeForProcess<object>(async processInfo => await func(processInfo), filter, artifactType);

            return result.Result;
        }

        private Task<ActionResult<T>> InvokeForProcess<T>(Func<IProcessInfo, ActionResult<T>> func, ProcessFilter? filter, string artifactType = null)
        {
            return InvokeForProcess(processInfo => Task.FromResult(func(processInfo)), filter, artifactType);
        }

        private async Task<ActionResult<T>> InvokeForProcess<T>(Func<IProcessInfo, Task<ActionResult<T>>> func, ProcessFilter? filter, string artifactType = null)
        {
            IDisposable artifactTypeRegistration = null;
            if (!string.IsNullOrEmpty(artifactType))
            {
                KeyValueLogScope artifactTypeScope = new KeyValueLogScope();
                artifactTypeScope.AddArtifactType(artifactType);
                artifactTypeRegistration = _logger.BeginScope(artifactTypeScope);
            }

            try
            {
                return await this.InvokeService(async () =>
                {
                    IProcessInfo processInfo = await _diagnosticServices.GetProcessAsync(filter, HttpContext.RequestAborted);

                    KeyValueLogScope processInfoScope = new KeyValueLogScope();
                    processInfoScope.AddEndpointInfo(processInfo.EndpointInfo);
                    using var _ = _logger.BeginScope(processInfoScope);

                    _logger.ResolvedTargetProcess();

                    return await func(processInfo);
                }, _logger);
            }
            finally
            {
                artifactTypeRegistration?.Dispose();
            }
        }
    }
}
