using System;
using System.Threading;

namespace Tracee
{
    class Program
    {
        static void Main(string[] args)
        {
            for (var i = 0; i < 10; i++)
            {
                Console.WriteLine("Hello World!");
                Thread.Sleep(500);
            }
            Thread.Sleep(5000);
        }
    }
}
