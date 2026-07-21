// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Xunit;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class MultiplexTestOutputHelper : ITestOutputHelper
    {
        private readonly ITestOutputHelper[] _outputs;

        public MultiplexTestOutputHelper(params ITestOutputHelper[] outputs)
        {
            _outputs = outputs;
        }

        public string Output
        {
            get
            {
                StringBuilder builder = new();
                foreach (ITestOutputHelper output in _outputs)
                {
                    builder.Append(output.Output);
                }
                return builder.ToString();
            }
        }

        public void Write(string message)
        {
            foreach (ITestOutputHelper output in _outputs)
            {
                output.Write(message);
            }
        }

        public void Write(string format, params object[] args)
        {
            foreach (ITestOutputHelper output in _outputs)
            {
                output.Write(format, args);
            }
        }

        public void WriteLine(string message)
        {
            foreach (ITestOutputHelper output in _outputs)
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
