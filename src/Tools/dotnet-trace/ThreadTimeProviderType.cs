// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal enum ThreadTimeProviderType
    {
        SampleProfiler,
        UniversalEvents,
        Unknown
    }

    internal static class ThreadTimeStackSourceHelper
    {
        public static MutableTraceEventStackSource GenerateStackSourceFromTrace(string traceFile, bool includeEventSourceEvents = false, bool continueOnError = false)
        {
            string etlxFilePath = TraceLog.CreateFromEventPipeDataFile(traceFile, null, new TraceLogOptions() { ContinueOnError = continueOnError });
            using SymbolReader symbolReader = new(TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath };
            using TraceLog eventLog = new(etlxFilePath);
            MutableTraceEventStackSource stackSource = new(eventLog);

            ThreadTimeProviderType providerType = DetectProviderType(eventLog);
            switch (providerType)
            {
                case ThreadTimeProviderType.SampleProfiler:
                {
                    stackSource.OnlyManagedCodeStacks = true;
                    SampleProfilerThreadTimeComputer computer = new(eventLog, symbolReader)
                    {
                        IncludeEventSourceEvents = includeEventSourceEvents,
                    };
                    computer.GenerateThreadTimeStacks(stackSource);
                    break;
                }
                case ThreadTimeProviderType.UniversalEvents:
                {
                    stackSource.OnlyManagedCodeStacks = false;
#pragma warning disable 618
                    ThreadTimeStackComputer computer = new(eventLog, symbolReader)
                    {
                        IncludeEventSourceEvents = false,
                    };
                    computer.GenerateThreadTimeStacks(stackSource);
#pragma warning restore 618
                    break;
                }
                case ThreadTimeProviderType.Unknown:
                default:
                    throw new DiagnosticToolException("The trace does not contain SampleProfiler or Universal.Events data required for thread-time analysis.");
            }

            if (File.Exists(etlxFilePath))
            {
                File.Delete(etlxFilePath);
            }

            return stackSource;
        }

        private static ThreadTimeProviderType DetectProviderType(TraceLog eventLog)
        {
            foreach (TraceEvent evt in eventLog.Events)
            {
                if (string.Equals(evt.ProviderName, "Microsoft-DotNETCore-SampleProfiler", StringComparison.Ordinal))
                {
                    return ThreadTimeProviderType.SampleProfiler;
                }
                if (string.Equals(evt.ProviderName, "Universal.Events", StringComparison.Ordinal))
                {
                    return ThreadTimeProviderType.UniversalEvents;
                }
            }

            return ThreadTimeProviderType.Unknown;
        }
    }
}
