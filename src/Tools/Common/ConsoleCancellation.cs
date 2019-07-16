// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace Microsoft.Internal.Utilities
{
    public static class ConsoleCancellationExtensions
    {
        public static CancellationToken GetCtrlCToken(this IConsole console)
        {
            var cts = new CancellationTokenSource();
            console.CancelKeyPress += (sender, args) =>
            {
                if (cts.IsCancellationRequested)
                {
                    // Terminate forcibly, the user pressed Ctrl-C a second time
                    args.Cancel = false;
                }
                else
                {
                    // Don't terminate, just trip the token
                    args.Cancel = true;
                    cts.Cancel();
                }
            };
            return cts.Token;
        }

        public static Task WaitForCtrlCAsync(this IConsole console)
        {
            var tcs = new TaskCompletionSource<object>();
            console.CancelKeyPress += (sender, args) =>
            {
                // Don't terminate, just trip the task
                args.Cancel = true;
                tcs.TrySetResult(null);
            };
            return tcs.Task;
        }
    }
}
