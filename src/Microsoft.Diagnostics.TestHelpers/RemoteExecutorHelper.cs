// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.DotNet.RemoteExecutor;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Diagnostics.TestHelpers
{
    public static class RemoteExecutorHelper
    {
        public static async Task<int> RemoteInvoke(ITestOutputHelper output, TestConfiguration config, TimeSpan timeout, string dumpPath, Func<string, Task<int>> method)
        {
            RemoteInvokeOptions options = new()
            {
                StartInfo = new ProcessStartInfo() { RedirectStandardOutput = true, RedirectStandardError = true }
            };
            // The remoteInvokeHandle is NOT disposed (i.e. with a using) here because the RemoteExecutor dispose code uses an older (1.x) version
            // of clrmd that conflicts with the 2.0 version diagnostics is using and throws the exception:
            //
            // "Method not found: 'Microsoft.Diagnostics.Runtime.DataTarget Microsoft.Diagnostics.Runtime.DataTarget.AttachToProcess(Int32, UInt32)'."
            //
            // When RemoteExecutor is fixed the "using" can be added and the GC.SuppressFinalize be removed.
            RemoteInvokeHandle remoteInvokeHandle = RemoteExecutor.Invoke(method, config.Serialize(), options);
            GC.SuppressFinalize(remoteInvokeHandle);
            try
            {
                Task stdOutputTask = WriteStreamToOutput(remoteInvokeHandle.Process.StandardOutput, output);
                Task stdErrorTask = WriteStreamToOutput(remoteInvokeHandle.Process.StandardError, output);
                Task outputTasks = Task.WhenAll(stdErrorTask, stdOutputTask);

                Task processExit = Task.Factory.StartNew(
                    remoteInvokeHandle.Process.WaitForExit,
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                Task timeoutTask = Task.Delay(timeout);
                Task completedTask = await Task.WhenAny(outputTasks, processExit, timeoutTask).ConfigureAwait(false);
                if (completedTask == timeoutTask)
                {
                    if (!string.IsNullOrEmpty(dumpPath))
                    {
                        output.WriteLine($"RemoteExecutorHelper.RemoteInvoke timed out: writing dump to {dumpPath}");
                        DiagnosticsClient client = new(remoteInvokeHandle.Process.Id);
                        try
                        {
                            await client.WriteDumpAsync(DumpType.WithHeap, dumpPath, WriteDumpFlags.None, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is ArgumentException or UnsupportedCommandException or ServerErrorException)
                        {
                            output.WriteLine($"RemoteExecutorHelper.RemoteInvoke: writing dump FAILED {ex}");
                        }
                    }
                    throw new XunitException("RemoteExecutorHelper.RemoteInvoke timed out");
                }
                else
                {
                    return remoteInvokeHandle.ExitCode;
                }
            }
            finally
            {
                if (remoteInvokeHandle.Process != null)
                {
                    try
                    {
                        output.WriteLine($"RemoteExecutorHelper.RemoteInvoke: killing process {remoteInvokeHandle.Process.Id}");
                        remoteInvokeHandle.Process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }
                    remoteInvokeHandle.Process.Dispose();
                    remoteInvokeHandle.Process = null;
                }
            }
        }

        public static async Task RemoteInvoke(ITestOutputHelper output, Action testCase)
        {
            RemoteInvokeOptions options = new()
            {
                StartInfo = new ProcessStartInfo() { RedirectStandardOutput = true, RedirectStandardError = true }
            };
            using RemoteInvokeHandle remoteInvokeHandle = RemoteExecutor.Invoke(testCase, options);

            Task stdOutputTask = WriteStreamToOutput(remoteInvokeHandle.Process.StandardOutput, output);
            Task stdErrorTask = WriteStreamToOutput(remoteInvokeHandle.Process.StandardError, output);
            await Task.WhenAll(stdErrorTask, stdOutputTask).ConfigureAwait(false);
        }

        private static Task<Task> WriteStreamToOutput(StreamReader reader, ITestOutputHelper output)
        {
            return Task.Factory.StartNew(async () => {
                try
                {
                    while (!reader.EndOfStream)
                    {
                        string line = await reader.ReadLineAsync().ConfigureAwait(false);
                        output.WriteLine(line);
                    }
                }
                catch (ObjectDisposedException)
                {
                    output.WriteLine("Failed to collect remote process's output");
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}
