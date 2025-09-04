// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Xunit.Extensions
{
    public class SkippableFactMessageBus : IMessageBus
    {
        private readonly IMessageBus innerBus;

        public SkippableFactMessageBus(IMessageBus innerBus)
        {
            this.innerBus = innerBus;
        }

        public int DynamicallySkippedTestCount { get; private set; }

        public void Dispose() { }

        public bool QueueMessage(IMessageSinkMessage message)
        {
            ITestFailed testFailed = message as ITestFailed;
            if (testFailed != null)
            {
                string exceptionType = testFailed.ExceptionTypes.FirstOrDefault();
                if (exceptionType == typeof(SkipTestException).FullName)
                {
                    DynamicallySkippedTestCount++;
                    return innerBus.QueueMessage(new TestSkipped(testFailed.Test, testFailed.Messages.FirstOrDefault()));
                }
            }

            // Nothing we care about, send it on its way
            return innerBus.QueueMessage(message);
        }
    }
}
