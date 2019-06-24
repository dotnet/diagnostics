// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing.Etlx;

namespace Microsoft.Diagnostics.Tools.Analyze
{
    public class AnalysisSession
    {
        public MemoryDump Dump { get; }
        public TraceLog Trace { get; }

        public AnalysisSession(MemoryDump dump, TraceLog trace)
        {
            Dump = dump;
            Trace = trace;
        }
    }
}
