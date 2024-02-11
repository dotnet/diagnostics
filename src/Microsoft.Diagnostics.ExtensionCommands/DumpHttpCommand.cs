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

        // Field names to get System.Net.Http.HttpRequestMessage.Method
        private static readonly string[] s_httpMethodFieldNames =["_method", "method"];

        // Field names to get System.Net.Http.HttpMethod.Method
        private static readonly string[] s_methodFieldNames =["_method", "method"];

        private static readonly string[] s_requestMessageFieldNames =["_requestMessage", "requestMessage"];
        private static readonly string[] s_statusCodeFieldNames =["_statusCode", "statusCode"];
        private static readonly string[] s_requestUriFieldNames =["_requestUri", "requestUri"];
        private static readonly string[] s_uriStringFieldNames =["_string", "m_String"];

        /// <summary>Gets whether to summarize all httpRequests found rather than showing detailed info.</summary>
        [Option(Name = "--stats", Help = "Summarize all HTTP requests found rather than showing detailed info.")]
        public bool Summarize { get; set; }

        /// <summary>Gets whether to show only requests without response.</summary>
        [Option(Name = "--pending", Help = "Show only requests without response")]
        public bool Pending { get; set; }

        /// <summary>Gets whether to show only requests with response.</summary>
        [Option(Name = "--completed", Help = "Show only requests with response")]
        public bool Completed { get; set; }

        /// <summary>Gets whether to show only requests with with specified request uri.</summary>
        [Option(Name = "--uri", Help = "Show only requests with with specified request uri")]
        public string? Uri { get; set; }

        /// <summary>Gets whether to show only requests with with specified response status codei.</summary>
        [Option(Name = "--statuscode", Help = "Show only requests with with specified response status code")]
        public int? StatusCode { get; set; }

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
            httpRequests = FilterByOptions(httpRequests);

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

                int total = 0;
                foreach (HttpRequestInfo httpRequest in httpRequests)
                {
                    output.WriteRow(httpRequest.Address, httpRequest.MethodTable, httpRequest.HttpMethod, httpRequest.StatusCode, httpRequest.Url);
                    total++;
                }
                WriteLine($"Total {total} requests");
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
                    ClrObject request = ReadAnyObjectField(clrObject, s_requestMessageFieldNames, "Unable to read HttpResponseMessage");
                    yield return BuildRequest(request, clrObject);
                }

                // TODO handle System.Net.HttpWebRequest for .NET Framework dumps
            }
        }

        private static HttpRequestInfo BuildRequest(ClrObject request, ClrObject? response)
        {
            string httpMethod = GetHttpMethod(request);
            string uri = GetRequestUri(request);
            int? statusCode = response != null ? ReadAnyField<int>(response.Value, s_statusCodeFieldNames, "Unable to read status code") : null;

            return new HttpRequestInfo(
                request.Address,
                request.Type!.MethodTable,
                httpMethod,
                uri, statusCode);
        }

        private static string GetHttpMethod(ClrObject request)
        {
            ClrObject httpMethodObject = ReadAnyObjectField(request, s_httpMethodFieldNames, "Unable to read HTTP Method");
            return ReadAnyObjectField(httpMethodObject, s_methodFieldNames, "Unable to read Method").AsString()!;
        }

        private static string GetRequestUri(ClrObject request)
        {
            ClrObject requestUriObject = ReadAnyObjectField(request, s_requestUriFieldNames, "Unable to read request uri");
            return ReadAnyObjectField(requestUriObject, s_uriStringFieldNames, "Unable to read uri string").AsString()!;
        }

        private static T ReadAnyField<T>(ClrObject clrObject, string[] fieldNames, string errorMessage)
            where T : unmanaged
        {
            foreach (string fieldName in fieldNames)
            {
                if (clrObject.TryReadField(fieldName, out T result))
                {
                    return result;
                }
            }

            throw new ArgumentException(BuildMissingFieldMessage(clrObject, fieldNames, errorMessage));
        }

        private static ClrObject ReadAnyObjectField(ClrObject clrObject, string[] fieldNames, string errorMessage)
        {
            foreach (string fieldName in fieldNames)
            {
                if (clrObject.TryReadObjectField(fieldName, out ClrObject result))
                {
                    return result;
                }
            }

            throw new ArgumentException(BuildMissingFieldMessage(clrObject, fieldNames, errorMessage));
        }

        private static string BuildMissingFieldMessage(ClrObject clrObject, string[] fieldNames, string errorMessage)
        {
            return $"{errorMessage}. Type '{clrObject.Type?.Name}' does not contain any field named {string.Join(" or ", fieldNames)}";
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

        private IEnumerable<HttpRequestInfo> FilterByOptions(IEnumerable<HttpRequestInfo> requests)
        {
            foreach (HttpRequestInfo request in requests)
            {
                bool matchesPendingCompletedFilter = !Pending && !Completed
                                                     || Pending && request.StatusCode == null
                                                     || Completed && request.StatusCode != null;

                bool matchesUriFilter = Uri == null || request.Url.IndexOf(Uri, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchesStatusCodeFilter = StatusCode == null || request.StatusCode == StatusCode;

                if (matchesPendingCompletedFilter && matchesUriFilter && matchesStatusCodeFilter)
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
    Summarize all http requests:                                dumphttp --stats
    Show only completed http requests:                          dumphttp --completed
    Show failed request with request uri contains weather:      dumphttp --statuscode 500 --uri weather
";

        private sealed class HttpRequestInfo
        {
            public ulong Address { get; }
            public ulong MethodTable { get; }
            public string HttpMethod { get; }
            public int? StatusCode { get; }
            public string Url { get; }
            public string Host { get; }

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
