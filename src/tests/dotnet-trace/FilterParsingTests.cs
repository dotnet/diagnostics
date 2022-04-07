// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using Xunit;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.Trace
{
    public class FilterParsingTests
    {

        [Theory]
        [MemberData(nameof(TestCases))]
        public void CorrectlyParsesAndAppliesFilter(string filter, IEnumerable<(string,string,int)> events, int filteredEventCount)
        {
            SimpleLogger.Log.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Information;
            using MockMultiEventStream stream = new(events);
            using EventPipeEventSource source = new(stream);
            Func<TraceEvent, bool> predicate = PredicateBuilder.ParseFilter(filter);
            (Dictionary<string, int> stats, int total, string _, string _, string _) = StatReportHandler.CollectStats(source, predicate);
            Assert.Equal(filteredEventCount, stats.Values.Sum());
        }


        static private readonly List<(string, string, int)> SimpleTrace = new() { ("Provider1", "Event1", 5), ("Provider1", "Event2", 5), ("Provider2", "Event1", 5), ("Provider2", "Event2", 5) };

        public static IEnumerable<object[]> TestCases()
        {
            yield return new object[] { "", SimpleTrace, 20 };
            yield return new object[] { "Provider1", SimpleTrace, 10 };
            yield return new object[] { "Provider2", SimpleTrace, 10 };
            yield return new object[] { "Provider2:name=Event2", SimpleTrace, 5 };
            yield return new object[] { "-Provider1", SimpleTrace, 10 };
            yield return new object[] { "-Provider1;Provider2:id=3", SimpleTrace, 5 };
        }
    }
}