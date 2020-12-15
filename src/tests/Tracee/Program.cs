// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;

namespace Tracee
{
    class Program
    {
        private const int LoopCount = 30;

        static void Main(string[] args)
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
