using System;
using System.Runtime.CompilerServices;

namespace LineNums
{
    class Program
    {
        static void Main(string[] args)
        {
            Foo();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Foo()
        {
            Bar();
            Bar();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Bar()
        {
            while (true)
            {
                throw new Exception("This should be line #25");
            }
        }
    }
}
