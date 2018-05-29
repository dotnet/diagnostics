using System;
using System.Text;
using Xunit.Abstractions;

namespace Debugger.Tests
{
    public class MultiplexTestOutputHelper : ITestOutputHelper
    {
        readonly ITestOutputHelper[] _outputs;

        public MultiplexTestOutputHelper(params ITestOutputHelper[] outputs)
        {
            _outputs = outputs;
        }

        public void WriteLine(string message)
        {
            foreach(ITestOutputHelper output in _outputs)
            {
                output.WriteLine(message);
            }
        }

        public void WriteLine(string format, params object[] args)
        {
            foreach (ITestOutputHelper output in _outputs)
            {
                output.WriteLine(format, args);
            }
        }
    }
}