// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.SymbolStore;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Simple trace/logging support.
    /// </summary>
    public sealed class Tracer : ITracer
    {
        public static bool Enable { get; set; }

        public static ITracer Instance { get; } = Enable ? new Tracer() : new NullTracer();

        private Tracer()
        {
        }

        public void WriteLine(string message)
        {
            Trace.WriteLine(message);
            Trace.Flush();
        }

        public void WriteLine(string format, params object[] arguments)
        {
            WriteLine(string.Format(format, arguments));
        }

        public void Information(string message)
        {
            Trace.TraceInformation(message);
            Trace.Flush();
        }

        public void Information(string format, params object[] arguments)
        {
            Trace.TraceInformation(format, arguments);
            Trace.Flush();
        }

        public void Warning(string message)
        {
            Trace.TraceWarning(message);
            Trace.Flush();
        }

        public void Warning(string format, params object[] arguments)
        {
            Trace.TraceWarning(format, arguments);
            Trace.Flush();
        }

        public void Error(string message)
        {
            Trace.TraceError(message);
            Trace.Flush();
        }

        public void Error(string format, params object[] arguments)
        {
            Trace.TraceError(format, arguments);
            Trace.Flush();
        }

        public void Verbose(string message)
        {
            Information(message);
        }

        public void Verbose(string format, params object[] arguments)
        {
            Information(format, arguments);
        }

        private sealed class NullTracer : ITracer
        {
            internal NullTracer()
            {
            }

            public void WriteLine(string message)
            {
            }

            public void WriteLine(string format, params object[] arguments)
            {
            }

            public void Information(string message)
            {
            }

            public void Information(string format, params object[] arguments)
            {
            }

            public void Warning(string message)
            {
            }

            public void Warning(string format, params object[] arguments)
            {
            }

            public void Error(string message)
            {
            }

            public void Error(string format, params object[] arguments)
            {
            }

            public void Verbose(string message)
            {
            }

            public void Verbose(string format, params object[] arguments)
            {
            }
        }
    }
}
