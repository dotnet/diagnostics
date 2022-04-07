// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Tools.Common
{
    internal class SimpleLogger
    {
        public static SimpleLogger Log { get; private set; } = new();
        public LogLevel MinimumLevel { get; set; } = LogLevel.Error;

        public void LogError(string message) => WriteLine(LogLevel.Error, "ERROR", message);

        public void LogInfo(string message) => WriteLine(LogLevel.Information, "INFO", message);

        private void WriteLine(LogLevel level, string prefix, string message)
        {
            if (level >= MinimumLevel)
                Console.WriteLine($"[{DateTime.Now:hh:mm:ss.fff}] {prefix} :: {message}");
        }
    }
}