// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = CommandName, Aliases = new string[] { "DumpHttp" }, Help = "Displays information about HTTP requests")]
    public sealed class DumpHttpCommand : ClrRuntimeCommandBase
    {
        /// <summary>The name of the command.</summary>
        private const string CommandName = "dumphttp";

        /// <summary>Gets whether to summarize all httpRequests found rather than showing detailed info.</summary>
        [Option(Name = "--stats", Help = "Summarize all HTTP requests found rather than showing detailed info.")]
        public bool Summarize { get; set; }

        /// <summary>Gets whether to show only requests without response.</summary>
        [Option(Name = "--pending", Help = "Show only requests without response")]
        public bool Pending { get; set; }

        /// <summary>Gets whether to show only requests with response.</summary>
        [Option(Name = "--completed", Help = "Show only requests with response")]
        public bool Completed { get; set; }

        private HeapWithFilters? FilteredHeap { get; set; }

        private static readonly Column s_httpMethodColumn = new(Align.Left, 6, Formats.Text);
        private static readonly Column s_statusCodeColumn = new(Align.Right, 10, Formats.Integer);

        /// <summary>Invokes the command.</summary>
        public override void Invoke()
        {
            ParseArguments();

            // Enumerate the heap, gathering up all relevant HTTP request related httpRequests.
            IEnumerable<HttpRequestInfo> httpRequests = CollectHttpRequests();
            httpRequests = FilterDuplicates(httpRequests);
            if (Pending || Completed)
            {
                httpRequests = FilterByStatus(httpRequests);
            }

            // Render the data according to the options specified.
            if (Summarize)
            {
                RenderStats();
            }
            else
            {
                RenderRequests();
            }

            return;

            // <summary>Group httpRequests and summarize how many of each occurred.</summary>
            void RenderStats()
            {
                Dictionary<HttpRequestStatGroupKey, HttpRequestStat> statCounts = new();

                foreach (HttpRequestInfo httpRequest in httpRequests)
                {
                    HttpRequestStatGroupKey statKey = httpRequest.GetGroupKey();
                    if (statCounts.TryGetValue(statKey, out HttpRequestStat stat))
                    {
                        stat.AddRequest();
                    }
                    else
                    {
                        stat = new HttpRequestStat(httpRequest);
                        statCounts.Add(statKey, stat);
                    }
                }

                WriteLine("Statistics:");
                Table output = new(Console, ColumnKind.Integer, s_httpMethodColumn, s_statusCodeColumn, ColumnKind.Text);
                output.WriteHeader("Count", "Method", "StatusCode", "Host");

                foreach (KeyValuePair<HttpRequestStatGroupKey, HttpRequestStat> entry in statCounts.OrderByDescending(s => s.Value.Count))
                {
                    output.WriteRow(entry.Value.Count, entry.Value.HttpMethod, entry.Value.StatusCode, entry.Value.Host);
                }

                int total = statCounts.Select(stat => stat.Value.Count).Sum();
                WriteLine($"Total {total} requests");
            }

            // <summary>Render each http request.</summary>
            void RenderRequests()
            {
                Table output = new(Console, ColumnKind.Pointer, ColumnKind.Pointer, s_httpMethodColumn, s_statusCodeColumn, ColumnKind.Text);
                output.WriteHeader("Address", "MethodTable", "Method", "StatusCode", "Uri");

                foreach (HttpRequestInfo httpRequest in httpRequests)
                {
                    output.WriteRow(httpRequest.Address, httpRequest.MethodTable, httpRequest.HttpMethod, httpRequest.StatusCode, httpRequest.Url);
                }
            }
        }

        private IEnumerable<HttpRequestInfo> CollectHttpRequests()
        {
            IEnumerable<ClrObject> objectsToPrint = FilteredHeap!.EnumerateFilteredObjects(Console.CancellationToken);

            foreach (ClrObject clrObject in objectsToPrint)
            {
                if (clrObject.Type?.Name == "System.Net.Http.HttpRequestMessage")
                {
                    // HttpRequestMessage doesn't have reference to HttpResponseMessage
                    // the same http request can be found by HttpResponseMessage
                    // these duplicates handled by FilterDuplicates method
                    yield return BuildRequest(clrObject, null);
                }

                if (clrObject.Type?.Name == "System.Net.Http.HttpResponseMessage")
                {
                    ClrObject request = GetRequest(clrObject);
                    yield return BuildRequest(request, clrObject);
                }

                // TODO handle System.Net.HttpWebRequest for .NET Framework dumps
            }

            yield break;

            HttpRequestInfo BuildRequest(ClrObject request, ClrObject? response)
            {
                string httpMethod = GetHttpMethod(request);
                string uri = GetUri(request);
                int? statusCode = response != null ? GetStatusCode(response.Value) : null;

                return new HttpRequestInfo(
                    request.Address,
                    request.Type!.MethodTable,
                    httpMethod,
                    uri, statusCode);
            }

            string GetHttpMethod(ClrObject request)
            {
                return Runtime.ClrInfo.Flavor == ClrFlavor.Core
                    ? request.ReadObjectField("_method").ReadStringField("_method")!
                    : request.ReadObjectField("method").ReadStringField("method")!;
            }

            ClrObject GetRequest(ClrObject request)
            {
                return Runtime.ClrInfo.Flavor == ClrFlavor.Core
                    ? request.ReadObjectField("_requestMessage")
                    : request.ReadObjectField("requestMessage");
            }

            string GetUri(ClrObject request)
            {
                return Runtime.ClrInfo.Flavor == ClrFlavor.Core
                    ? request.ReadObjectField("_requestUri").ReadStringField("_string")!
                    : request.ReadObjectField("requestUri").ReadStringField("m_String")!;
            }

            int GetStatusCode(ClrObject response)
            {
                return Runtime.ClrInfo.Flavor == ClrFlavor.Core
                    ? response.ReadField<int>("_statusCode")
                    : response.ReadField<int>("statusCode");
            }
        }

        /// <summary>
        /// Filter duplicates from requests collection
        /// </summary>
        /// <remarks>
        /// Filter out requests found by HttpRequestMessage only.
        /// Requests found by HttpResponseMessage+HttpRequestMessage have more filled props.
        /// </remarks>
        private static IEnumerable<HttpRequestInfo> FilterDuplicates(IEnumerable<HttpRequestInfo> requests)
        {
            HashSet<ulong> processedRequests = new();

            foreach (HttpRequestInfo request in requests.OrderBy(r => r.StatusCode == null))
            {
                if (!processedRequests.Add(request.Address))
                {
                    continue;
                }

                yield return request;
            }
        }

        private IEnumerable<HttpRequestInfo> FilterByStatus(IEnumerable<HttpRequestInfo> requests)
        {
            foreach (HttpRequestInfo request in requests)
            {
                bool matchFilter = Pending && request.StatusCode == null
                                   || Completed && request.StatusCode != null;

                if (matchFilter)
                {
                    yield return request;
                }
            }
        }

        private void ParseArguments()
        {
            if (Pending && Completed)
            {
                Pending = false;
                Completed = false;
            }

            FilteredHeap = new HeapWithFilters(Runtime.Heap);
        }

        /// <summary>Gets detailed help for the command.</summary>
        [HelpInvoke]
        public static string GetDetailedHelp() =>
            @"Examples:
    Summarize all http requests:            dumphttp --stats
    Show only completed http requests:      dumphttp --completed
";

        private sealed class HttpRequestInfo
        {
            public ulong Address { get; }
            public ulong MethodTable { get; }
            public string HttpMethod { get; }
            public int? StatusCode{ get; }
            public string Url { get; }
            public string Host{ get; }

            // TODO add response content-type header?
            // TODO add response length? (can be difficult to calculate)

            public HttpRequestInfo(ulong address, ulong methodTable, string httpMethod, string url, int? statusCode)
            {
                Address = address;
                MethodTable = methodTable;
                HttpMethod = httpMethod;
                Url = url;
                Host = new Uri(url).Host;
                StatusCode = statusCode;
            }

            public HttpRequestStatGroupKey GetGroupKey() => new(StatusCode, HttpMethod, Host);
        }

        private sealed class HttpRequestStat
        {
            public int Count { get; private set; }
            public string Host { get; }
            public string HttpMethod { get; }
            public int? StatusCode { get; }

            public HttpRequestStat(HttpRequestInfo request)
            {
                Count = 1;
                Host = new Uri(request.Url).Host;
                HttpMethod = request.HttpMethod;
                StatusCode = request.StatusCode;
            }

            public void AddRequest()
            {
                Count++;
            }
        }

        private record struct HttpRequestStatGroupKey(int? StatusCode, string HttpMethod, string Host);
    }
}
