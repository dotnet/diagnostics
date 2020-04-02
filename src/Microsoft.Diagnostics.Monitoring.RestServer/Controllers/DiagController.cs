using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Controllers
{
    [Route("api/[controller]")]
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
        public async Task<ActionResult> GetDump(int pid, [FromQuery]int? dumpType = 1)
        {
            if (!Enum.IsDefined(typeof(DumpType), dumpType))
            {
                return BadRequest("Invalid dump type");
            }

            try
            {
                Stream dump = await _diagnosticServices.GetDump(pid, (DumpType)dumpType);

                //Compression is done automatically by the response
                //Chunking is done because the result has no content-length
                return File(dump, "application/octet-stream", fileDownloadName: FormattableString.Invariant($"coredump_{pid}"));
            }
            catch (DiagnosticsClientException e)
            {
                return BadRequest(e.Message);
            }
            catch(InvalidOperationException e)
            {
                return BadRequest(e.Message);
            }
        }

        [HttpPost("{pid}/trace")]
        public async Task<ActionResult> Trace(int pid, [FromBody]TraceRequest request)
        {
            try
            {
                if (request.State == TraceState.Running)
                {
                    Stream stream = await _diagnosticServices.StartNetTrace(pid, request);
                    return File(stream, "application/octet-stream", fileDownloadName: FormattableString.Invariant($"{Guid.NewGuid()}.nettrace"));
                }
                else
                {
                    await _diagnosticServices.StopNetTrace(pid, request);
                    return Ok();
                }
            }
            catch (DiagnosticsClientException e)
            {
                return BadRequest(e.Message);
            }
            catch(InvalidOperationException e)
            {
                return BadRequest(e.Message);
            }

        }
    }
}
