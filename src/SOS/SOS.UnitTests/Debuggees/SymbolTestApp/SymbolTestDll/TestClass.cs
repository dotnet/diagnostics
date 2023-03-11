// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;

namespace SymbolTestDll
{
    public class TestClass
    {
        public static int ThrowException(string argument)
        {
            Foo5(56, argument);
            return 0;
        }

        private static int Foo5(int x, string argument)
        {
            return Foo6(x, argument);
        }

        private static int Foo6(int x, string argument)
        {
            Foo7(argument);
            return x;
        }

        private static void Foo7(string argument)
        {
            if (argument != null)
            {
                throw new Exception(argument);
            }
            else
            {
                Thread.Sleep(-1);
            }
        }
    }
}
