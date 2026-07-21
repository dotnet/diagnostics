// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Xunit;

namespace Microsoft.Diagnostics.TestHelpers
{
    /// <summary>
    /// An ITestOutputHelper implementation that logs to a file
    /// </summary>
    public class FileTestOutputHelper : ITestOutputHelper, IDisposable
    {
        private readonly StreamWriter _logWriter;
        private readonly object _lock;

        public FileTestOutputHelper(string logFilePath, FileMode fileMode = FileMode.Create)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
            FileStream fs = new(logFilePath, fileMode);
            _logWriter = new StreamWriter(fs);
            _logWriter.AutoFlush = true;
            _lock = new object();
        }

        public string Output => string.Empty;

        public void Write(string message)
        {
            lock (_lock)
            {
                _logWriter.Write(message);
            }
        }

        public void Write(string format, params object[] args)
        {
            lock (_lock)
            {
                _logWriter.Write(format, args);
            }
        }

        public void WriteLine(string message)
        {
            lock (_lock)
            {
                _logWriter.WriteLine(message);
            }
        }

        public void WriteLine(string format, params object[] args)
        {
            lock (_lock)
            {
                _logWriter.WriteLine(format, args);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _logWriter.Dispose();
            }
        }
    }
}
