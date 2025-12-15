// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.Diagnostics.Tools.Common;

namespace Microsoft.Diagnostics.Tools.Common
{
    /// <summary>
    /// The default implementation of IConsole maps everything to System.Console. In the future
    /// maybe we could map it to System.CommandLine's IConsole, but right now that interface doesn't
    /// have enough functionality for everything we need.
    /// </summary>
    internal class DefaultConsole : IConsole
    {
        private readonly bool _useAnsi;
        public DefaultConsole(bool useAnsi = false)
        {
            _useAnsi = useAnsi;
        }

        public int WindowHeight => Console.WindowHeight;

        public int WindowWidth => Console.WindowWidth;

        // Not all platforms implement this and that is OK. Callers need to be prepared for NotSupportedException
#pragma warning disable CA1416
        public bool CursorVisible { get => Console.CursorVisible; set { Console.CursorVisible = value; } }
#pragma warning restore CA1416

        public int CursorLeft => Console.CursorLeft;

        public int CursorTop => Console.CursorTop;

        public int BufferWidth => Console.BufferWidth;

        public int BufferHeight => Console.BufferHeight;

        public bool IsOutputRedirected => Console.IsOutputRedirected;

        public bool IsInputRedirected => Console.IsInputRedirected;

        public bool KeyAvailable => Console.KeyAvailable;

        public TextWriter Out => Console.Out;

        public TextWriter Error => Console.Error;

        public void Clear()
        {
            if (_useAnsi)
            {
                Write($"\u001b[H\u001b[J");
            }
            else
            {
                Console.Clear();
            }
        }

        public void SetCursorPosition(int col, int row)
        {
            if (_useAnsi)
            {
                Write($"\u001b[{row + 1};{col + 1}H");
            }
            else
            {
                Console.SetCursorPosition(col, row);
            }
        }
        public void Write(string text) => Console.Write(text);
        public void WriteLine(string text) => Console.WriteLine(text);
        public void WriteLine() => Console.WriteLine();
        public ConsoleKeyInfo ReadKey() => Console.ReadKey();
        public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
    }
}
