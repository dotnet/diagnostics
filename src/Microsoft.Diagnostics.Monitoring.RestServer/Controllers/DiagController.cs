// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
                int pidValue = _diagnosticServices.ResolveProcess(pid);
                Stream result = await _diagnosticServices.GetDump(pidValue, type);

                FormattableString dumpFileName;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // This assumes that Windows does not have shared process spaces
                    Process process = Process.GetProcessById(pidValue);
                    dumpFileName = $"{process.ProcessName}.{pidValue}.dmp";
                }
                else
                {
                    dumpFileName = $"core_{pidValue}";
                }

                //Compression is done automatically by the response
                //Chunking is done because the result has no content-length
                return File(result, "application/octet-stream", Invariant(dumpFileName));
            });
        }

        [HttpGet("cpuprofile/{pid?}")]
        public Task<ActionResult> CpuProfile(int? pid, [FromQuery][Range(-1, int.MaxValue)]int durationSeconds = 30)
        {
            TimeSpan duration = ConvertSecondsToTimeSpan(durationSeconds);
            return InvokeService(async () =>
            {
                int pidValue = _diagnosticServices.ResolveProcess(pid);
                IStreamWithCleanup result = await _diagnosticServices.StartCpuTrace(pidValue, duration, this.HttpContext.RequestAborted);
                return new StreamWithCleanupResult(result, "application/octet-stream", Invariant($"{Guid.NewGuid()}.nettrace"));
            });
        }

        [HttpGet("trace/{pid?}")]
        public Task<ActionResult> Trace(int? pid, [FromQuery][Range(-1, int.MaxValue)]int durationSeconds = 30)
        {
            TimeSpan duration = ConvertSecondsToTimeSpan(durationSeconds);
            return InvokeService(async () =>
            {
                int pidValue = _diagnosticServices.ResolveProcess(pid);
                IStreamWithCleanup result = await _diagnosticServices.StartTrace(pidValue, duration, this.HttpContext.RequestAborted);
                return new StreamWithCleanupResult(result, "application/octet-stream", Invariant($"{Guid.NewGuid()}.nettrace"));
            });
        }

        [HttpGet("logs/{pid?}")]
        public ActionResult Logs(int? pid, [FromQuery][Range(-1, int.MaxValue)]int durationSeconds = 30)
        {
            TimeSpan duration = ConvertSecondsToTimeSpan(durationSeconds);
            return InvokeService(() =>
            {
                int pidValue = _diagnosticServices.ResolveProcess(pid);
                return new OutputStreamResult(async (outputStream, token) =>
                {
                    await _diagnosticServices.StartLogs(outputStream, pidValue, duration, token);
                }, "application/x-ndjson", Invariant($"{Guid.NewGuid()}.txt"));
            });
        }

        private ActionResult InvokeService(Func<ActionResult> serviceCall)
        {
            //We can convert ActionResult to ActionResult<T>
            //and then safely convert back.
            return InvokeService<object>(() => serviceCall()).Result;
        }

        private ActionResult<T> InvokeService<T>(Func<ActionResult<T>> serviceCall)
        {
            //Convert from ActionResult<T> to Task<ActionResult<T>>
            //and safely convert back.
            return InvokeService(() => Task.FromResult(serviceCall())).Result;
        }

        private async Task<ActionResult> InvokeService(Func<Task<ActionResult>> serviceCall)
        {
            //Task<ActionResult> -> Task<ActionResult<T>>
            //Then unwrap the result back to ActionResult
            ActionResult<object> result = await InvokeService<object>(async () => await serviceCall());
            return result.Result;
        }

        private async Task<ActionResult<T>> InvokeService<T>(Func<Task<ActionResult<T>>> serviceCall)
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

        private static TimeSpan ConvertSecondsToTimeSpan(int durationSeconds)
        {
            return durationSeconds < 0 ?
                Timeout.InfiniteTimeSpan :
                TimeSpan.FromSeconds(durationSeconds);
        }
    }
}
