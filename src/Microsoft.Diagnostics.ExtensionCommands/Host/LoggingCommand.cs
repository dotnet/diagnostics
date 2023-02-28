// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "logging", Help = "Enables/disables internal diagnostic logging.", Flags = CommandFlags.Global)]
    public class LoggingCommand : CommandBase
    {
        [ServiceImport(Optional = true)]
        public IDiagnosticLoggingService DiagnosticLoggingService { get; set; }

        [Argument(Name = "path", Help = "Log file path.")]
        public string FilePath { get; set; }

        [Option(Name = "--enable", Aliases = new string[] { "enable", "-e" }, Help = "Enable internal logging.")]
        public bool Enable { get; set; }

        [Option(Name = "--disable", Aliases = new string[] { "disable", "-d" }, Help = "Disable internal logging.")]
        public bool Disable { get; set; }

        public override void Invoke()
        {
            if (DiagnosticLoggingService is null)
            {
                throw new DiagnosticsException("Diagnostic logging is not supported");
            }
            if (Disable)
            {
                DiagnosticLoggingService.Disable();
            }
            else if (Enable || !string.IsNullOrWhiteSpace(FilePath))
            {
                DiagnosticLoggingService.Enable(FilePath);
            }
            WriteLine("Logging is {0}", DiagnosticLoggingService.IsEnabled ? "enabled" : "disabled");

            if (!string.IsNullOrWhiteSpace(DiagnosticLoggingService.FilePath))
            {
                WriteLine(DiagnosticLoggingService.FilePath);
            }
        }
    }
}
