// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "logopen", Help = "Enables console file logging.")]
    [Command(Name = "logclose", DefaultOptions = "--disable", Help = "Disables console file logging.")]
    public class ConsoleLoggingCommand : CommandBase
    {
        [ServiceImport(Optional = true)]
        public IConsoleFileLoggingService FileLoggingService { get; set; }

        [Argument(Name = "path", Help = "Log file path.")]
        public string FilePath { get; set; }

        [Option(Name = "--disable", Help = "Disable console file logging.")]
        public bool Disable { get; set; }

        public override void Invoke()
        {
            if (FileLoggingService is null)
            {
                throw new DiagnosticsException("Console logging is not supported");
            }
            if (Disable)
            {
                FileLoggingService.Disable();
            }
            else if (!string.IsNullOrWhiteSpace(FilePath))
            {
                FileLoggingService.Enable(FilePath);
            }
            string filePath = FileLoggingService.FilePath;
            if (filePath is not null)
            {
                WriteLine($"Console is logging to {filePath}");
            }
            else
            {
                WriteLine("Console logging is disabled");
            }
        }
    }
}
