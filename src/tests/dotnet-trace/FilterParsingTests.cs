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
        [InlineData("Microsoft-Windows-DotNETRuntime:name=*gc*", 48114)]
        [InlineData("Microsoft-Windows-DotNETRuntime:name=SuspendEEStart", 12027)]
        [InlineData("", 200822)]
        public void CorrectlyParsesAndAppliesFilter(string filter, int filteredEventCount)
        {
            using EventPipeEventSource source = new("./Traces/TEST_simpletrace.nettrace");
            Func<TraceEvent, bool> predicate = PredicateBuilder.ParseFilter(filter);
            (Dictionary<string, int> stats, int total, string _, string _, string _) = StatReportHandler.CollectStats(source, predicate);
            Assert.Equal(filteredEventCount, stats.Values.Sum());
        }
    }
}