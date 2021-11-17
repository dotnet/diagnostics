// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
                EnableLogging();
            }
            else if (Disable) {
                DisableLogging();
            }
            WriteLine("Logging is {0}", Trace.Listeners[ListenerName] != null ? "enabled" : "disabled");
        }

        public static void Initialize()
        {
            if (Environment.GetEnvironmentVariable("DOTNET_ENABLED_SOS_LOGGING") == "1")
            {
                EnableLogging();
            }
        }

        public static void EnableLogging()
        {
            if (Trace.Listeners[ListenerName] == null)
            {
                Trace.Listeners.Add(new LoggingListener());
                Trace.AutoFlush = true;
            }
        }

        public static void DisableLogging()
        {
            Trace.Listeners.Remove(ListenerName);
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
