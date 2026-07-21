// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Xunit;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class ConsoleTestOutputHelper : ITestOutputHelper
    {
        public string Output => string.Empty;

        public void Write(string message)
        {
            Console.Write(message);
            Console.Out.Flush();
        }

        public void Write(string format, params object[] args)
        {
            Console.Write(format, args);
            Console.Out.Flush();
        }

        public void WriteLine(string message)
        {
            Console.WriteLine(message);
            Console.Out.Flush();
        }

        public void WriteLine(string format, params object[] args)
        {
            Console.WriteLine(format, args);
            Console.Out.Flush();
        }
    }
}
