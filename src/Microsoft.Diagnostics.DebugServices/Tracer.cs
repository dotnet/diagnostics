// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Simple trace/logging support.
    /// </summary>
    public sealed class Tracer : Microsoft.SymbolStore.ITracer
    {
        public static Microsoft.SymbolStore.ITracer Instance { get; } = new Tracer();

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
    }
}
