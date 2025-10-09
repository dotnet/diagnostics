// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Diagnostics.Tools.Common;
using Xunit;

namespace Microsoft.Diagnostics.Tests.Common
{
    internal class MockConsole : TextWriter, IConsole
    {
        char[][] _chars;

        int _cursorLeft;

        public MockConsole(int width, int height)
        {
            WindowWidth = BufferWidth = width;
            WindowHeight = height;
            Clear();
        }

        public override Encoding Encoding => Encoding.UTF8;

        public int WindowHeight { get; init; }

        public int WindowWidth { get; init; }

        public bool CursorVisible { get; set; }

        public int CursorLeft { get; private set; }

        public int CursorTop { get; private set; }

        public int BufferWidth { get; private set; }

        public int BufferHeight { get; private set; }

        public bool IsOutputRedirected { get; set; }

        public bool IsInputRedirected { get; private set; }

        public bool KeyAvailable { get; private set; }

        public TextWriter Out => this;

        public TextWriter Error => this;

        public void Clear()
        {
            _chars = new char[WindowHeight][];
            for(int i = 0; i < WindowHeight; i++)
            {
                _chars[i] = new char[WindowWidth];
                for(int j = 0; j < WindowWidth; j++)
                {
                    _chars[i][j] = ' ';
                }
            }
            CursorTop = 0;
            _cursorLeft = 0;
        }
        public void SetCursorPosition(int col, int row)
        {
            CursorTop = row;
            _cursorLeft = col;
        }
        public override void Write(string text)
        {
            for(int textPos = 0; textPos < text.Length; )
            {
                // This attempts to mirror the behavior of System.Console
                // if the console width is X then it is possible to write X characters and still have the console
                // report you are on the same line. If the X+1'th character isn't a newline then the console automatically
                // wraps and writes that character at the beginning of the next line leaving the cursor at index 1. If
                // the X+1'th character is a newline then the cursor moves to the next line at index 0.
                Debug.Assert(_cursorLeft <= WindowWidth);
                if(text.AsSpan(textPos).StartsWith(Environment.NewLine))
                {
                    textPos += Environment.NewLine.Length;
                    _cursorLeft = 0;
                    CursorTop++;
                }
                else
                {
                    if (_cursorLeft == WindowWidth)
                    {
                        _cursorLeft = 0;
                        CursorTop++;
                    }
                    // make sure we are writing inside the legal buffer area, if not we'll hit the exception below
                    if (CursorTop < WindowHeight)
                    {
                        _chars[CursorTop][_cursorLeft] = text[textPos];
                        textPos++;
                        _cursorLeft++;
                    }
                }
                if (CursorTop >= WindowHeight)
                {
                    // For now we assume that no test case intentionally scrolls the buffer. If we want to have tests that
                    // scroll the buffer by design then update this implementation.
                    throw new Exception("Writing beyond the end of the console buffer would have caused text to scroll.");
                }
            }
        }
        public override void WriteLine(string text)
        {
            Write(text);
            Write(Environment.NewLine);
        }
        public override void WriteLine() => Write(Environment.NewLine);

        public string GetLineText(int row) => new string(_chars[row]).TrimEnd();

        public ConsoleKeyInfo ReadKey() => Console.ReadKey();

        public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);

        public string[] Lines
        {
            get
            {
                string[] lines = new string[WindowHeight];
                for(int i = 0; i < WindowHeight; i++)
                {
                    lines[i] = GetLineText(i);
                }
                return lines;
            }
        }

        public void AssertLinesEqual(params string[] expectedLines) => AssertLinesEqual(0, expectedLines);

        public void AssertLinesEqual(int startLine, params string[] expectedLines)
        {
            if (startLine + expectedLines.Length > Lines.Length)
            {
                Assert.Fail("MockConsole output had fewer output lines than expected." + Environment.NewLine +
                    $"Expected line count: {expectedLines.Length}" + Environment.NewLine +
                    $"Actual line count: {Lines.Length}");
            }
            for (int i = 0; i < expectedLines.Length; i++)
            {

                string actualLine = GetLineText(startLine+i);
                string expectedLine = expectedLines[i];
                if(actualLine != expectedLine)
                {
                    Assert.Fail("MockConsole output did not match expected output." + Environment.NewLine +
                        $"Expected line {startLine + i,2}: {expectedLine}" + Environment.NewLine +
                        $"Actual line     : {actualLine}");
                }

            }
        }

        public void AssertSanitizedLinesEqual(Func<string[], string[]> sanitizer, params string[] expectedLines)
        {
            string[] actualLines = Lines;
            if (sanitizer is not null)
            {
                actualLines = sanitizer(actualLines);
            }
            Assert.True(actualLines.Length >= expectedLines.Length, "Sanitized console output had fewer lines than expected." + Environment.NewLine +
                $"Expected line count: {expectedLines.Length}" + Environment.NewLine +
                $"Actual line count: {actualLines.Length}");

            for (int i = 0; i < expectedLines.Length; i++)
            {
                if (!string.Equals(expectedLines[i], actualLines[i], StringComparison.Ordinal))
                {
                    Assert.Fail("Sanitized console output mismatch." + Environment.NewLine +
                        $"Line {i,2} Expected: {expectedLines[i]}" + Environment.NewLine +
                        $"Line {i,2} Actual  : {actualLines[i]}");
                }
            }
            for (int i = expectedLines.Length; i < actualLines.Length; i++)
            {
                Assert.True(string.IsNullOrEmpty(actualLines[i]), $"Actual line #{i} beyond expected lines is not empty: {actualLines[i]}");
            }
        }
    }
}
