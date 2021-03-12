// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text;

namespace Microsoft.Diagnostics.Monitoring
{
    internal class CommandLineHelper
    {
        public static string ExtractExecutablePath(string commandLine, bool isWindows)
        {
            if (string.IsNullOrEmpty(commandLine))
            {
                return commandLine;
            }

            int commandLineLength = commandLine.Length;
            bool isQuoted = false;
            bool isEscaped = false;
            int i = 0;
            char c = commandLine[0];

            // Search for the first whitespace character that is not quoted.
            // Store character literals as it iterates the command line. Escaped
            // characters within double quotes are unescaped for non-Windows systems.
            // Algorithm based on INIT_FormatCommandLine behavior from
            // https://github.com/dotnet/runtime/blob/main/src/coreclr/pal/src/init/pal.cpp
            StringBuilder builder = new StringBuilder(commandLineLength);
            do
            {
                if (isEscaped)
                {
                    builder.Append(c);
                    isEscaped = false;
                }
                else if (c == '"')
                {
                    isQuoted = !isQuoted;
                }
                else if (c == '\\' && !isWindows)
                {
                    if (isQuoted)
                    {
                        isEscaped = true;
                    }
                    else
                    {
                        builder.Append(c);
                    }
                }
                else
                {
                    builder.Append(c);
                }

                if (commandLineLength == ++i)
                {
                    break;
                }

                c = commandLine[i];
            }
            while (isQuoted || !char.IsWhiteSpace(c));

            return builder.ToString();
        }
    }
}
