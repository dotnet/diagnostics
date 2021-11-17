// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    /// <summary>
    /// An implementation of ITestOutputHelper that adds one indent level to
    /// the start of each line
    /// </summary>
    public class IndentedTestOutputHelper : ITestOutputHelper
    {
        readonly string _indentText;
        readonly ITestOutputHelper _output;

        public IndentedTestOutputHelper(ITestOutputHelper innerOutput, string indentText = "    ")
        {
            _output = innerOutput;
            _indentText = indentText;
        }

        public void WriteLine(string message)
        {
            _output.WriteLine(_indentText + message);
        }

        public void WriteLine(string format, params object[] args)
        {
            _output.WriteLine(_indentText + format, args);
        }
    }
}
