// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public class DiagnosticLoggingService : IDiagnosticLoggingService
    {
        private const string ListenerName = "SOS.LoggingListener";
        private IConsoleService _consoleService;
        private IConsoleFileLoggingService _fileLoggingService;
        private StreamWriter _writer;

        public static DiagnosticLoggingService Instance { get; } = new DiagnosticLoggingService();

        private DiagnosticLoggingService()
        {
        }

        #region IDiagnosticLoggingService

        /// <summary>
        /// Returns true if logging to console or file
        /// </summary>
        public bool IsEnabled => Trace.Listeners[ListenerName] is not null;

        /// <summary>
        /// The file path if logging to file.
        /// </summary>
        public string FilePath => (_writer?.BaseStream as FileStream)?.Name;

        /// <summary>
        /// Enable diagnostics logging.
        /// </summary>
        /// <param name="filePath">log file path or null if log to console</param>
        /// <remarks>see File.Open for possible exceptions thrown</remarks>
        public void Enable(string filePath)
        {
            if (filePath is not null)
            {
                FileStream stream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                CloseLogging();
                _writer = new StreamWriter(stream) {
                    AutoFlush = true
                };
                _fileLoggingService?.AddStream(stream);
            }
            if (Trace.Listeners[ListenerName] is null)
            {
                Trace.Listeners.Add(new LoggingListener(this));
                Trace.AutoFlush = true;
            }
        }

        /// <summary>
        /// Disable diagnostics logging (close if logging to file).
        /// </summary>
        public void Disable()
        {
            CloseLogging();
            Trace.Listeners.Remove(ListenerName);
        }

        #endregion

        /// <summary>
        /// Initializes the diagnostic logging service.  Reads the DOTNET_ENABLED_SOS_LOGGING
        /// environment variable to log to console or file.
        /// </summary>
        /// <param name="logfile"></param>
        public static void Initialize(string logfile = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(logfile))
                {
                    logfile = Environment.GetEnvironmentVariable("DOTNET_ENABLED_SOS_LOGGING");
                }
                if (!string.IsNullOrWhiteSpace(logfile))
                {
                    Instance.Enable(logfile == "1" ? null : logfile);
                }
            }
            catch (Exception ex) when ( ex is IOException || ex is NotSupportedException || ex is SecurityException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Sets the console service and the console file logging control service.
        /// </summary>
        /// <param name="consoleService">This is used for to log to the console</param>
        /// <param name="fileLoggingService">This is used to hook the command console output to write the diagnostic log file.</param>
        public void SetConsole(IConsoleService consoleService, IConsoleFileLoggingService fileLoggingService = null)
        {
            _consoleService = consoleService;
            _fileLoggingService = fileLoggingService;
        }

        private void CloseLogging()
        {
            if (_writer is not null)
            {
                _fileLoggingService?.RemoveStream(_writer.BaseStream);
                _writer.Flush();
                _writer.Close();
                _writer = null;
            }
        }

        private class LoggingListener : TraceListener
        {
            private readonly DiagnosticLoggingService _diagnosticLoggingService;

            internal LoggingListener(DiagnosticLoggingService diagnosticLoggingService)
                : base(ListenerName)
            {
                _diagnosticLoggingService = diagnosticLoggingService;
            }

            public override void Close()
            {
                _diagnosticLoggingService.CloseLogging();
                base.Close();
            }

            public override void Write(string message)
            {
                if (_diagnosticLoggingService._writer is not null)
                {
                    try
                    {
                        _diagnosticLoggingService._writer.Write(message);
                        return;
                    }
                    catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is NotSupportedException)
                    {
                    }
                }
                _diagnosticLoggingService._consoleService?.Write(message);
            }

            public override void WriteLine(string message)
            {
                if (_diagnosticLoggingService._writer is not null)
                {
                    try
                    {
                        _diagnosticLoggingService._writer.WriteLine(message);
                        return;
                    }
                    catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is NotSupportedException)
                    {
                    }
                }
                _diagnosticLoggingService._consoleService?.WriteLine(message);
            }
        }
    }
}
