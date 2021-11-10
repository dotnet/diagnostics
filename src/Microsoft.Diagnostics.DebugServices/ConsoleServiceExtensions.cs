// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class ConsoleServiceExtensions
    {
        /// <summary>
        /// Display text
        /// </summary>
        /// <param name="console">console service instance</param>
        /// <param name="message">text message</param>
        public static void WriteLine(this IConsoleService console, string message)
        {
            console.Write(message + Environment.NewLine);
        }

        /// <summary>
        /// Display formatted text
        /// </summary>
        /// <param name="console">console service instance</param>
        /// <param name="format">format string</param>
        /// <param name="args">arguments</param>
        public static void WriteLine(this IConsoleService console, string format, params object[] args)
        {
            console.Write(string.Format(format, args) + Environment.NewLine);
        }

        /// <summary>
        /// Display formatted warning text
        /// </summary>
        /// <param name="console">console service instance</param>
        /// <param name="format">format string</param>
        /// <param name="args">arguments</param>
        public static void WriteLineWarning(this IConsoleService console, string format, params object[] args)
        {
            console.WriteWarning(string.Format(format, args) + Environment.NewLine);
        }

        /// <summary>
        /// Display formatted error text
        /// </summary>
        /// <param name="console">console service instance</param>
        /// <param name="format">format string</param>
        /// <param name="args">arguments</param>
        public static void WriteLineError(this IConsoleService console, string format, params object[] args)
        {
            console.WriteError(string.Format(format, args) + Environment.NewLine);
        }
    }
}
