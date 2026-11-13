// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Tests.Common;
using Microsoft.Internal.Common.Utils;
using Xunit;

namespace Microsoft.Diagnostics.Tools.Trace
{
    public class LineRewriterTests
    {
        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 1)]
        public void SupportIsIndependentForDifferentConsoles(int firstHeight, int secondHeight)
        {
            LineRewriter first = new(new MockConsole(10, firstHeight)) { LineToClear = 1 };
            LineRewriter second = new(new MockConsole(10, secondHeight)) { LineToClear = 1 };

            Assert.Equal(firstHeight > 1, first.IsRewriteConsoleLineSupported);
            Assert.Equal(secondHeight > 1, second.IsRewriteConsoleLineSupported);
            Assert.Equal(firstHeight > 1, first.IsRewriteConsoleLineSupported);
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(1, -1)]
        public void SupportIsIndependentForDifferentRewriters(int firstLine, int secondLine)
        {
            MockConsole console = new(10, 4);
            LineRewriter first = new(console) { LineToClear = firstLine };
            LineRewriter second = new(console) { LineToClear = secondLine };

            Assert.Equal(firstLine >= 0, first.IsRewriteConsoleLineSupported);
            Assert.Equal(secondLine >= 0, second.IsRewriteConsoleLineSupported);
            Assert.Equal(firstLine >= 0, first.IsRewriteConsoleLineSupported);
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(1, -1)]
        [InlineData(4, 1)]
        [InlineData(1, 4)]
        public void ChangingLineToClearRechecksSupport(int firstLine, int secondLine)
        {
            MockConsole console = new(10, 4);
            console.SetCursorPosition(0, 3);
            LineRewriter rewriter = new(console) { LineToClear = firstLine };

            Assert.Equal(firstLine >= 0 && firstLine < 4, rewriter.IsRewriteConsoleLineSupported);
            Assert.Equal(3, console.CursorTop);

            rewriter.LineToClear = secondLine;

            Assert.Equal(secondLine >= 0 && secondLine < 4, rewriter.IsRewriteConsoleLineSupported);
            Assert.Equal(3, console.CursorTop);
        }
    }
}
