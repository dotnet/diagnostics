// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.CommandLine.IO;

namespace System.CommandLine
{
    internal static class StandardStreamWriterExtensions
    {
        /// <summary>
        /// Shim for removed member from System.CommandLine.Experimental > System.CommandLine
        /// </summary>
        public static void WriteLine(this IStandardStreamWriter output, string value)
        {
            output.Write(value);
            output.Write(Environment.NewLine);
        }
    }
}