using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Diagnostics.Tracing.Parsers.FrameworkEventSource;
using Newtonsoft.Json;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Controllers
{
    /// <summary>
    /// This is temporary UI for testing.
    /// </summary>
    public class DashboardController : Controller
    {
        public Task<IActionResult> Index()
        {
            return SharedView();
        }

        public async Task<IActionResult> StartTrace(int pid)
        {
            var client = new HttpClient();
            TraceRequest request = new TraceRequest { State = TraceState.Running };

            //We do not request compression here from the api, since we are local.
            using HttpRequestMessage postRequest = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:52323/api/diag/{pid}/trace");
            postRequest.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(postRequest, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var resultStream = await response.Content.ReadAsStreamAsync();

            //TODO We need to cleanup the response/client here, but we can't do so until the file is streamed by the client.
            return File(resultStream, "application/octet-stream", response.Content.Headers.ContentDisposition.FileName);
        }

        public async Task<IActionResult> StopTrace(int pid)
        {
            using var client = new HttpClient();
            TraceRequest request = new TraceRequest() { State =  TraceState.Stopped };

            using var response = await client.PostAsJsonAsync($"http://localhost:52323/api/diag/{pid}/trace", request);

            response.EnsureSuccessStatusCode();

            return await SharedView();
        }

        private async Task<IActionResult> SharedView()
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync("http://localhost:52323/api/diag/getpids");

            int[] pids = await ExtractJson<int[]>(response);
            this.ViewData["Pids"] = new List<int>(pids);

            return View("Index");
        }

        private static async Task<T> ExtractJson<T>(HttpResponseMessage responseMessage)
        {
            responseMessage.EnsureSuccessStatusCode();
            using var responseStream = await responseMessage.Content.ReadAsStreamAsync();
            T result = await System.Text.Json.JsonSerializer.DeserializeAsync<T>(responseStream);
            return result;
        }
    }
}
