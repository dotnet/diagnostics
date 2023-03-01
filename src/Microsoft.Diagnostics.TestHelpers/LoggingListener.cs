// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class LoggingListener : TraceListener
    {
        private readonly CharToLineConverter _converter;

        public static void EnableListener(ITestOutputHelper output, string name)
        {
            if (Trace.Listeners[name] == null)
            {
                Trace.Listeners.Add(new LoggingListener(output, name));
                Trace.AutoFlush = true;
            }
        }

        public static void EnableConsoleListener(string name)
        {
            if (Trace.Listeners[name] == null)
            {
                Trace.Listeners.Add(new LoggingListener(name));
                Trace.AutoFlush = true;
            }
        }

        private LoggingListener(ITestOutputHelper output, string name)
            : base(name)
        {
            _converter = new CharToLineConverter((text) => {
                output.WriteLine(text);
            });
        }

        private LoggingListener(string name)
            : base(name)
        {
            _converter = new CharToLineConverter((text) => {
                Console.WriteLine(text);
            });
        }

        public override void Write(string message)
        {
            _converter.Input(message);
        }

        public override void WriteLine(string message)
        {
            _converter.Input(message + Environment.NewLine);
        }
    }
}
