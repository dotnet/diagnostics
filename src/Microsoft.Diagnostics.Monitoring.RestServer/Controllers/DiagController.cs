using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Controllers
{
    [Route("api/v1/[controller]")]
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

        [HttpGet("getpids")]
        public ActionResult<IEnumerable<string>> GetPids()
        {
            return new JsonResult(_diagnosticServices.GetProcesses());
        }

        [HttpGet(@"{pid}/dump")]
        public Task<ActionResult> GetDump(int pid, [FromQuery]int? dumpType = 1)
        {
            if (!Enum.IsDefined(typeof(DumpType), dumpType))
            {
                return Task.FromResult<ActionResult>(BadRequest(new ProblemDetails { Detail = "Invalid dump type", Status = 400 }));
            }

            return InvokeService(async () =>
            {
                Stream dump = await _diagnosticServices.GetDump(pid, (DumpType)dumpType);

                //Compression is done automatically by the response
                //Chunking is done because the result has no content-length
                return File(dump, "application/octet-stream", fileDownloadName: FormattableString.Invariant($"coredump_{pid}"));
            });
        }

        [HttpGet("{pid}/cpuprofile")]
        public Task<ActionResult> CpuProfile(int pid, [FromQuery]int durationSeconds = 30) 
        {
            return InvokeService(async () =>
            {
                Stream stream = await _diagnosticServices.StartCpuTrace(pid, durationSeconds);
                return File(stream, "application/octet-stream", fileDownloadName: FormattableString.Invariant($"{Guid.NewGuid()}.nettrace"));
            });
        }

        private async Task<ActionResult> InvokeService(Func<Task<ActionResult>> serviceCall)
        {
            try
            {
                return await serviceCall();
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
            return new ProblemDetails { Detail = e.Message, Status = 400 };
        }
    }
}
