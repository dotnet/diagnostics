// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
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