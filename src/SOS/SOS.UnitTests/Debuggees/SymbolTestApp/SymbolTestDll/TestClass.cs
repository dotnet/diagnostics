using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SymbolTestDll
{
    public class TestClass
    {
        public static void ThrowException()
        {
            Foo5(56);
        }

        static int Foo5(int x)
        {
            return Foo6(x);
        }

        static int Foo6(int x)
        {
            Foo7();
            return x;
        }

        static void Foo7()
        {
            throw new Exception();
	}
    }
}
