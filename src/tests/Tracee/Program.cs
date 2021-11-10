// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;

namespace Tracee
{
    internal class Program
    {
        private const int LoopCount = 30;

        private static void Main(string[] args)
        {
            Console.WriteLine("Sleep in loop for {0} seconds.", LoopCount);

            // Runs for max of 30 sec
            for (var i = 0; i < LoopCount; i++)
            {
                Console.WriteLine("Iteration #{0}", i);
                Thread.Sleep(1000);
            }

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }
}
