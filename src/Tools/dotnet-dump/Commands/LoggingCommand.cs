// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Repl;
using System;
using System.CommandLine;
using System.Diagnostics;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "logging", Help = "Enable/disable internal logging")]
    public class LoggingCommand : CommandBase
    {
        [Option(Name = "enable", Help = "Enable internal logging.")]
        public bool Enable { get; set; }

        [Option(Name = "disable", Help = "Disable internal logging.")]
        public bool Disable { get; set; }

        private const string ListenerName = "Analyze.LoggingListener";

        public override void Invoke()
        {
            if (Enable) {
                if (Trace.Listeners[ListenerName] == null) {
                    Trace.Listeners.Add(new LoggingListener(Console));
                }
            }
            else if (Disable) {
                Trace.Listeners.Remove(ListenerName);
            }
            WriteLine("Logging is {0}", Trace.Listeners[ListenerName] != null ? "enabled" : "disabled");
        }

        class LoggingListener : TraceListener
        {
            private readonly IConsoleService _console;

            internal LoggingListener(IConsoleService console)
                : base(ListenerName)
            {
                _console = console;
            }

            public override void Write(string message)
            {
                _console.Write(message);
            }

            public override void WriteLine(string message)
            {
                _console.Write(message + Environment.NewLine);
            }
        }
    }
}
