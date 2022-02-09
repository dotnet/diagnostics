// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.RemoteExecutor;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    public static class RemoteExecutorHelper
    {
        public static async Task<int> RemoteInvoke(ITestOutputHelper output, TestConfiguration config, Func<string, Task<int>> method)
        {
            RemoteInvokeOptions options = new()
            {
                StartInfo = new ProcessStartInfo() { RedirectStandardOutput = true, RedirectStandardError = true }
            };
            int exitCode = 0;
            try
            {
                using RemoteInvokeHandle remoteInvokeHandle = RemoteExecutor.Invoke(method, config.Serialize(), options);
                try
                {
                    Task<string> stdOutputTask = remoteInvokeHandle.Process.StandardOutput.ReadToEndAsync();
                    Task<string> stdErrorTask = remoteInvokeHandle.Process.StandardError.ReadToEndAsync();
                    await Task.WhenAll(stdErrorTask, stdOutputTask);
                    output.WriteLine(stdOutputTask.Result);
                    output.WriteLine(stdErrorTask.Result);
                }
                catch (ObjectDisposedException)
                {
                    output.WriteLine("Failed to collect remote process's output");
                }
                remoteInvokeHandle.Process.WaitForExit();
                exitCode = remoteInvokeHandle.ExitCode;
            }
            // This is to catch this exception that is thrown when the remoteInvokeHandle is disposed. The RemoteExecutor dispose code uses an older (1.x)
            // version of clrmd that conflicits with the 2.0 version diagnostics is using.
            // "Method not found: 'Microsoft.Diagnostics.Runtime.DataTarget Microsoft.Diagnostics.Runtime.DataTarget.AttachToProcess(Int32, UInt32)'."
            catch (MissingMethodException)
            {
            }
            return exitCode;
        }

        public static async Task RemoteInvoke(ITestOutputHelper output, Action testCase)
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
                output.WriteLine(stdErrorTask.Result);
            }
            catch (ObjectDisposedException)
            {
                output.WriteLine("Failed to collect remote process's output");
            }
        }
    }
}
