using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
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
        private static readonly string DumpFileExtension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dmp" : string.Empty;

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
                OperationResult<Stream> result = await _diagnosticServices.GetDump(pid, type);

                //Compression is done automatically by the response
                //Chunking is done because the result has no content-length
                return CreateFileStreamResult(result, $"coredump_{result.Pid}{DumpFileExtension}");
            });
        }

        [HttpGet("cpuprofile/{pid?}")]
        public Task<ActionResult> CpuProfile(int? pid, [FromQuery]int duration = 30)
        {
            return InvokeService(async () =>
            {
                OperationResult<Stream> result = await _diagnosticServices.StartCpuTrace(pid, duration, this.HttpContext.RequestAborted);
                return CreateFileStreamResult(result, $"{Guid.NewGuid()}.nettrace");
            });
        }

        private FileStreamResult CreateFileStreamResult(OperationResult<Stream> result, FormattableString downloadName)
        {
            return File(result.Value, "application/octet-stream", Invariant(downloadName));
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
