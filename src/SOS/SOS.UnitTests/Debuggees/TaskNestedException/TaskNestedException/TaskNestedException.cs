// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using RandomTest;

namespace SosTests
{
    /// <summary>
    /// This test creates an asynchronous task that results in an exception being thrown.
    /// </summary>
    internal class TaskException
    {
        private static int Main()
        {
            RandomUserTask theTask = new RandomUserTask(100);
            theTask.WaitTask();

            return 0;
        }
    }
}