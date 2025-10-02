// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Diagnostics.Tools.Common.Exporters;
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

        public bool CursorVisible { get => throw new NotSupportedException(); set => throw new NotImplementedException(); }

        public int CursorTop { get; private set; }

        public int BufferWidth { get; private set; }

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

        // Asserts that the sanitized version of the console lines exactly equals the expected lines.
        // The sanitizer receives the raw Lines array and should return a transformed array (e.g., with
        // blank lines removed, whitespace collapsed, dynamic values normalized). Equality is ordinal.
        public void AssertSanitizedLinesEqual(Func<string[], string[]> sanitizer, params string[] expectedSanitizedLines)
        {
            if (sanitizer is null)
            {
                throw new ArgumentNullException(nameof(sanitizer));
            }
            string[] actualSanitized = sanitizer(Lines);
            if (actualSanitized.Length != expectedSanitizedLines.Length)
            {
                Assert.Fail("Sanitized console output length mismatch." + Environment.NewLine +
                    $"Expected: {expectedSanitizedLines.Length}" + Environment.NewLine +
                    $"Actual  : {actualSanitized.Length}");
            }
            for (int i = 0; i < expectedSanitizedLines.Length; i++)
            {
                string expected = expectedSanitizedLines[i];
                string actual = actualSanitized[i];
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    Assert.Fail("Sanitized console output mismatch." + Environment.NewLine +
                        $"Line {i,2} Expected: {expected}" + Environment.NewLine +
                        $"Line {i,2} Actual  : {actual}");
                }
            }
        }
    }
}
