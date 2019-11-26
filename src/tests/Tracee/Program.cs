// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;

namespace Tracee
{
    class Program
    {
        static void Main(string[] args)
        {
            // Runs for max of 30 sec
            for(var i = 0; i < 30; i++)
            {
                Thread.Sleep(1000);
            }
        }
    }
}
