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

        static int Foo5(int x, string argument)
        {
            return Foo6(x, argument);
        }

        static int Foo6(int x, string argument)
        {
            Foo7(argument);
            return x;
        }

        static void Foo7(string argument)
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
