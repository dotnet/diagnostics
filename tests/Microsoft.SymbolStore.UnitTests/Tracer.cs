// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit.Abstractions;

namespace Microsoft.SymbolStore.Tests
{
    /// <summary>
    /// Simple trace/logging support.
    /// </summary>
    internal sealed class Tracer : ITracer
    {
        private readonly ITestOutputHelper _output;

        public Tracer(ITestOutputHelper output)
        {
            _output = output;
        }

        public void WriteLine(string message)
        {
            _output.WriteLine(message);
        }

        public void WriteLine(string format, params object[] arguments)
        {
            _output.WriteLine(format, arguments);
        }

        public void Information(string message)
        {
            _output.WriteLine(message);
        }

        public void Information(string format, params object[] arguments)
        {
            _output.WriteLine(format, arguments);
        }

        public void Warning(string message)
        {
            _output.WriteLine("WARNING: " + message);
        }

        public void Warning(string format, params object[] arguments)
        {
            _output.WriteLine("WARNING: " + format, arguments);
        }

        public void Error(string message)
        {
            _output.WriteLine("ERROR: " + message);
        }

        public void Error(string format, params object[] arguments)
        {
            _output.WriteLine("ERROR: " + format, arguments);
        }

        public void Verbose(string message)
        {
        }

        public void Verbose(string format, params object[] arguments)
        {
        }
    }
}
