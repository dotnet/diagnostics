// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Xml.Linq;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class ConsoleServiceExtensions
    {
        /// <summary>
        /// Write text to console's standard out
        /// </summary>
        public static void Write(this IConsoleService console, string text) => console.WriteString(OutputType.Normal, text);

        /// <summary>
        /// Display a blank line
        /// </summary>
        public static void WriteLine(this IConsoleService console) => console.WriteString(OutputType.Normal, Environment.NewLine);

        /// <summary>
        /// Display text
        /// </summary>
        public static void WriteLine(this IConsoleService console, string text) => console.WriteString(OutputType.Normal, text + Environment.NewLine);

        /// <summary>
        /// Display formatted text
        /// </summary>
        public static void WriteLine(this IConsoleService console, string format, params object[] args) => console.WriteString(OutputType.Normal, string.Format(format, args) + Environment.NewLine);

        /// <summary>
        /// Write warning text to console
        /// </summary>
        public static void WriteWarning(this IConsoleService console, string text) => console.WriteString(OutputType.Warning, text);

        /// <summary>
        /// Display formatted warning text
        /// </summary>
        public static void WriteLineWarning(this IConsoleService console, string format, params object[] args) => console.WriteString(OutputType.Warning, string.Format(format, args) + Environment.NewLine);

        /// <summary>
        /// Write error text to console
        /// </summary>
        public static void WriteError(this IConsoleService console, string text) => console.WriteString(OutputType.Error, text);

        /// <summary>
        /// Display formatted error text
        /// </summary>
        public static void WriteLineError(this IConsoleService console, string format, params object[] args) => console.WriteString(OutputType.Error, string.Format(format, args) + Environment.NewLine);

        /// <summary>
        /// Writes Debugger Markup Language (DML) markup text
        /// </summary>
        public static void WriteDml(this IConsoleService console, string text) => console.WriteString(OutputType.Dml, text);

        /// <summary>
        /// Writes an exec tag to the output stream.
        /// </summary>
        /// <param name="console">console service instance</param>
        /// <param name="text">The display text.</param>
        /// <param name="cmd">The action to perform.</param>
        public static void WriteDmlExec(this IConsoleService console, string text, string cmd)
        {
            if (!console.SupportsDml || string.IsNullOrWhiteSpace(cmd))
            {
                console.WriteString(OutputType.Normal, text);
            }
            else
            {
                string dml = $"<exec cmd=\"{DmlEscape(cmd)}\">{DmlEscape(text)}</exec>";
                console.WriteString(OutputType.Dml, dml);
            }
        }

        private static string DmlEscape(string text) => string.IsNullOrWhiteSpace(text) ? text : new XText(text).ToString();
    }
}
