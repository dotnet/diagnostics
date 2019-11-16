using Microsoft.DotNet.RemoteExecutor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace eventpipe.UnitTests.common
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
