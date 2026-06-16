// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;

namespace LineNums
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Foo();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Foo()
        {
            Bar();
            Bar();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Bar()
        {
            while (true)
            {
                throw new Exception("This should be line #25");
            }
        }
    }
}
