// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Tools.Common;

namespace Microsoft.Internal.Common.Utils
{
    internal sealed class LineRewriter
    {
        public int LineToClear { get; set; }

        private IConsole Console { get; }

        public LineRewriter(IConsole console)
        {
            Console = console;
        }

        // ANSI escape codes:
        //  [2K => clear current line
        //  [{LineToClear};0H => move cursor to column 0 of row `LineToClear`
        public void RewriteConsoleLine()
        {
            bool useConsoleFallback = true;
            if (!Console.IsInputRedirected)
            {
                // in case of console input redirection, the control ANSI codes would appear

                // first attempt ANSI Codes
                int before = Console.CursorTop;
                Console.Out.Write($"\u001b[2K\u001b[{LineToClear};0H");
                int after = Console.CursorTop;

                // Some consoles claim to be VT100 compliant, but don't respect
                // all of the ANSI codes, so fallback to the System.Console impl in that case
                useConsoleFallback = (before == after);
            }

            if (useConsoleFallback)
            {
                SystemConsoleLineRewriter();
            }
        }

        private void SystemConsoleLineRewriter() => Console.SetCursorPosition(0, LineToClear);

        private static bool? _isSetCursorPositionSupported;
        public bool IsRewriteConsoleLineSupported
        {
            get
            {
                bool isSupported = _isSetCursorPositionSupported ?? EnsureInitialized();
                return isSupported;

                bool EnsureInitialized()
                {
                    try
                    {
                        int left = Console.CursorLeft;
                        int top = Console.CursorTop;
                        Console.SetCursorPosition(0, LineToClear);
                        Console.SetCursorPosition(left, top);
                        _isSetCursorPositionSupported = true;
                    }
                    catch
                    {
                        _isSetCursorPositionSupported = false;
                    }
                    return (bool)_isSetCursorPositionSupported;
                }
            }
        }
    }
}
