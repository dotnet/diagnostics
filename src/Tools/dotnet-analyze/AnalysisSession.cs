// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
