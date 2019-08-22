// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using System;
using System.Diagnostics;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "logging", Help = "Enable/disable internal logging", Platform = CommandPlatform.Global)]
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
                    Trace.Listeners.Add(new LoggingListener());
                    Trace.AutoFlush = true;
                }
            }
            else if (Disable) {
                Trace.Listeners.Remove(ListenerName);
            }
            WriteLine("Logging is {0}", Trace.Listeners[ListenerName] != null ? "enabled" : "disabled");
        }

        class LoggingListener : TraceListener
        {
            internal LoggingListener()
                : base(ListenerName)
            {
            }

            public override void Write(string message)
            {
                System.Console.Write(message);
            }

            public override void WriteLine(string message)
            {
                System.Console.WriteLine(message);
            }
        }
    }
}
