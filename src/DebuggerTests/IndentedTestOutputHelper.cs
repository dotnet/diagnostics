using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Debugger.Tests
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
