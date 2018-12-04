// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
