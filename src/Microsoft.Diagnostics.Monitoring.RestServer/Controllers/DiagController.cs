// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Diagnostics.Monitoring.RestServer.Models;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using static System.FormattableString;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Controllers
{
    [Route("")] // Root
    [ApiController]
    public class DiagController : ControllerBase
    {
        private const int MaxTraceSeconds = 60 * 5;

        private readonly ILogger<DiagController> _logger;
        private readonly IDiagnosticServices _diagnosticServices;
        private readonly IServiceProvider _serviceProvider;

        public DiagController(ILogger<DiagController> logger, IServiceProvider serviceProvider, IDiagnosticServices diagnosticServices)
        {
            _logger = logger;
            _diagnosticServices = diagnosticServices;
            _serviceProvider = serviceProvider;
        }

        [HttpGet("processes")]
        public ActionResult<IEnumerable<ProcessModel>> GetProcesses()
        {
            return InvokeService(() =>
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
        public Task<ActionResult> GetDump(int? pid, [FromQuery]DumpType type = DumpType.WithHeap)
        {
            return InvokeService(async () =>
            {
                OperationResult<Stream> result = await _diagnosticServices.GetDump(pid, type);

                FormattableString dumpFileName;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // This assumes that Windows does not have shared process spaces
                    Process process = Process.GetProcessById(result.Pid);
                    dumpFileName = $"{process.ProcessName}.{result.Pid}.dmp";
                }
                else
                {
                    dumpFileName = $"core_{result.Pid}";
                }

                //Compression is done automatically by the response
                //Chunking is done because the result has no content-length
                return File(result.Value, "application/octet-stream", Invariant(dumpFileName));
            });
        }

        [HttpGet("cpuprofile/{pid?}")]
        public Task<ActionResult> CpuProfile(int? pid, [FromQuery]int durationSeconds = 30)
        {
            return InvokeService(async () =>
            {
                ValidateDuration(durationSeconds);
                OperationResult<IStreamResult> result = await _diagnosticServices.StartCpuTrace(pid, durationSeconds, this.HttpContext.RequestAborted);
                return new EventStreamResult(result.Value, "application/octet-stream", Invariant($"{Guid.NewGuid()}.nettrace"));
            });
        }

        [HttpGet("trace/{pid?}")]
        public Task<ActionResult> Trace(int? pid, [FromQuery]int durationSeconds = 30)
        {
            return InvokeService(async () =>
            {
                ValidateDuration(durationSeconds);
                OperationResult<IStreamResult> result = await _diagnosticServices.StartTrace(pid, durationSeconds, this.HttpContext.RequestAborted);
                return new EventStreamResult(result.Value, "application/octet-stream", Invariant($"{Guid.NewGuid()}.nettrace"));
            });
        }

        [HttpGet("logs/{pid?}")]
        public ActionResult Logs(int? pid, [FromQuery] int durationSeconds = 30)
        {
            return InvokeService(() =>
            {
                ValidateDuration(durationSeconds);

                return new OutputStreamResult(async (outputStream, token) =>
                {
                    await _diagnosticServices.StartLogs(outputStream, pid, durationSeconds, token);
                }, "application/x-ndjson", Invariant($"{Guid.NewGuid()}.txt"));
            });
        }

        private static void ValidateDuration(int durationSeconds)
        {
            if ((durationSeconds < 1) || (durationSeconds > MaxTraceSeconds))
            {
                throw new InvalidOperationException("Invalid duration");
            }
        }

        private ActionResult InvokeService(Func<ActionResult> serviceCall)
        {
            try
            {
                return serviceCall();
            }
            catch (ArgumentException e)
            {
                return BadRequest(FromException(e));
            }
            catch (DiagnosticsClientException e)
            {
                return BadRequest(FromException(e));
            }
            catch (InvalidOperationException e)
            {
                return BadRequest(FromException(e));
            }
        }

        private ActionResult<T> InvokeService<T>(Func<ActionResult<T>> serviceCall)
        {
            try
            {
                return serviceCall();
            }
            catch (ArgumentException e)
            {
                return BadRequest(FromException(e));
            }
            catch (DiagnosticsClientException e)
            {
                return BadRequest(FromException(e));
            }
            catch (InvalidOperationException e)
            {
                return BadRequest(FromException(e));
            }
        }

        private async Task<ActionResult> InvokeService(Func<Task<ActionResult>> serviceCall)
        {
            try
            {
                return await serviceCall();
            }
            catch (ArgumentException e)
            {
                return BadRequest(FromException(e));
            }
            catch (DiagnosticsClientException e)
            {
                return BadRequest(FromException(e));
            }
            catch (InvalidOperationException e)
            {
                return BadRequest(FromException(e));
            }
        }

        private static ProblemDetails FromException(Exception e)
        {
            return new ProblemDetails
            {
                Detail = e.Message,
                Status = (int)HttpStatusCode.BadRequest
            };
        }
    }
}
