// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.RemoteExecutor;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace EventPipe.UnitTests.Common
{
    public static class RemoteTestExecutorHelper
    {
        public static async Task RunTestCaseAsync(Action testCase, ITestOutputHelper output)
        {
            var options = new RemoteInvokeOptions()
            {
                StartInfo = new ProcessStartInfo() { RedirectStandardOutput = true, RedirectStandardError = true }
            };

            using RemoteInvokeHandle remoteInvokeHandle = RemoteExecutor.Invoke(testCase, options);

            try
            {
                Task<string> stdOutputTask = remoteInvokeHandle.Process.StandardOutput.ReadToEndAsync();
                Task<string> stdErrorTask = remoteInvokeHandle.Process.StandardError.ReadToEndAsync();
                await Task.WhenAll(stdErrorTask, stdOutputTask);
                output.WriteLine(stdOutputTask.Result);
                Console.Error.Write(stdErrorTask.Result);
            }
            catch (ObjectDisposedException)
            {
                Console.Error.WriteLine("Failed to collect remote process's output");
            }
        }
    }
}
