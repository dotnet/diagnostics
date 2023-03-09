// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Log to file console service wrapper
    /// </summary>
    public class FileLoggingConsoleService : IConsoleService, IConsoleFileLoggingService, IDisposable
    {
        private readonly IConsoleService _consoleService;
        private readonly List<StreamWriter> _writers;
        private FileStream _consoleStream;

        public FileLoggingConsoleService(IConsoleService consoleService)
        {
            _consoleService = consoleService;
            _writers = new List<StreamWriter>();
        }

        public void Dispose() => Disable();

        #region IConsoleFileLoggingService

        /// <summary>
        /// The log file path if enabled, otherwise null.
        /// </summary>
        public string FilePath => _consoleStream?.Name;

        /// <summary>
        /// Enable console file logging.
        /// </summary>
        /// <param name="filePath">log file path</param>
        /// <remarks>see File.Open for more exceptions</remarks>
        public void Enable(string filePath)
        {
            FileStream consoleStream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            Disable();
            AddStream(consoleStream);
            _consoleStream = consoleStream;
        }

        /// <summary>
        /// Disable/close console file logging
        /// </summary>
        public void Disable()
        {
            if (_consoleStream is not null)
            {
                RemoveStream(_consoleStream);
                _consoleStream.Close();
                _consoleStream = null;
            }
        }

        /// <summary>
        /// Add to the list of file streams to write the console output.
        /// </summary>
        /// <param name="stream">Stream to add. Lifetime managed by caller.</param>
        public void AddStream(Stream stream)
        {
            Debug.Assert(stream is not null);
            _writers.Add(new StreamWriter(stream)
            {
                AutoFlush = true
            });
        }

        /// <summary>
        /// Remove the specified file stream from the writers.
        /// </summary>
        /// <param name="stream">Stream passed to add. Stream not closed or disposed.</param>
        public void RemoveStream(Stream stream)
        {
            if (stream is not null)
            {
                foreach (StreamWriter writer in _writers)
                {
                    if (writer.BaseStream == stream)
                    {
                        _writers.Remove(writer);
                        break;
                    }
                }
            }
        }

        #endregion

        #region IConsoleService

        public void Write(string text)
        {
            _consoleService.Write(text);
            foreach (StreamWriter writer in _writers)
            {
                try
                {
                    writer.Write(text);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException)
                {
                }
            }
        }

        public void WriteWarning(string text)
        {
            _consoleService.WriteWarning(text);
            foreach (StreamWriter writer in _writers)
            {
                try
                {
                    writer.Write(text);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException)
                {
                }
            }
        }

        public void WriteError(string text)
        {
            _consoleService.WriteError(text);
            foreach (StreamWriter writer in _writers)
            {
                try
                {
                    writer.Write(text);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException)
                {
                }
            }
        }

        public bool SupportsDml => _consoleService.SupportsDml;

        public void WriteDml(string text)
        {
            _consoleService.WriteDml(text);
            foreach (StreamWriter writer in _writers)
            {
                try
                {
                    // TODO: unwrap the DML?
                    writer.Write(text);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException)
                {
                }
            }
        }

        public void WriteDmlExec(string text, string action)
        {
            _consoleService.WriteDmlExec(text, action);

            foreach (StreamWriter writer in _writers)
            {
                try
                {
                    writer.Write(text);
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is NotSupportedException)
                {
                }
            }
        }

        public CancellationToken CancellationToken
        {
            get { return _consoleService.CancellationToken; }
            set { _consoleService.CancellationToken = value; }
        }

        public int WindowWidth => _consoleService.WindowWidth;

        #endregion
    }
}
