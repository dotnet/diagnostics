﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Tracee
{
    class Program
    {
        static int Main(string[] args)
        {
            foreach (string s in args)
                Console.WriteLine(s);

            return Int32.Parse(args[0]);
        }
    }
}
