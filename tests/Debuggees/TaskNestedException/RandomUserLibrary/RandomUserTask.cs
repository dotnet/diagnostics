// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;

namespace RandomTest
{
    public class RandomUserTask
    {
        public Task TryToDivideTask;

        private readonly int m_startingNumber;

        public RandomUserTask(int startingNumber)
        {
            m_startingNumber = startingNumber;
            TryToDivideTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    InnerException();
                }
                catch (Exception e)
                {
                    throw new FormatException("Bad format exception, outer.", e);
                }
            });
        }

        public void WaitTask()
        {
            TryToDivideTask.Wait();
        }

        public void InnerException()
        {
            throw new InvalidOperationException("This is an Inner InvalidOperationException.");
        }
    }
}
