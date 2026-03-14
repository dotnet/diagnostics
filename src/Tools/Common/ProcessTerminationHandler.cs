// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Internal.Common.Utils
{
    internal sealed class ProcessTerminationHandler : IDisposable
    {
        private bool _isDisposed;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly PosixSignalRegistration _sigIntRegistration;
        private readonly PosixSignalRegistration _sigQuitRegistration;
        private readonly PosixSignalRegistration _sigTermRegistration;
        private bool _blockSIGINT;
        private bool _blockSIGTERM;
        private bool _blockSIGQUIT;

        internal CancellationToken GetToken => _cancellationTokenSource.Token;

        internal static async Task<int> InvokeAsync(ParseResult parseResult, string blockedSignals = "")
        {
            using ProcessTerminationHandler terminationHandler = ConfigureTerminationHandler(parseResult, blockedSignals);
            return await parseResult.InvokeAsync(terminationHandler.GetToken).ConfigureAwait(false);
        }

        private static ProcessTerminationHandler ConfigureTerminationHandler(ParseResult parseResult, string blockedSignals)
        {
            // Use custom process terminate handler for the command line tool parse result.
            parseResult.Configuration.ProcessTerminationTimeout = null;
            return new ProcessTerminationHandler(blockedSignals);
        }

        private ProcessTerminationHandler(string blockedSignals)
        {
            _cancellationTokenSource = new();

            if (!string.IsNullOrEmpty(blockedSignals))
            {
                foreach (string signal in blockedSignals.Split(';'))
                {
                    if (signal.Equals("SIGINT", StringComparison.InvariantCultureIgnoreCase))
                    {
                        _blockSIGINT = true;
                    }
                    else if (signal.Equals("SIGQUIT", StringComparison.InvariantCultureIgnoreCase))
                    {
                        _blockSIGQUIT = true;
                    }
                    else if (signal.Equals("SIGTERM", StringComparison.InvariantCultureIgnoreCase))
                    {
                        _blockSIGTERM = true;
                    }
                }
            }

            _sigIntRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnPosixSignal);
            _sigQuitRegistration = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, OnPosixSignal);
            _sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnPosixSignal);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _sigIntRegistration?.Dispose();
                _sigQuitRegistration?.Dispose();
                _sigTermRegistration?.Dispose();

                GC.SuppressFinalize(this);
            }

            _isDisposed = true;
        }

        private void OnPosixSignal(PosixSignalContext context)
        {
            context.Cancel = true;

            if (_blockSIGINT && context.Signal == PosixSignal.SIGINT)
            {
                return;
            }
            else if (_blockSIGQUIT && context.Signal == PosixSignal.SIGQUIT)
            {
                return;
            }
            else if (_blockSIGTERM && context.Signal == PosixSignal.SIGTERM)
            {
                return;
            }

            Cancel();
        }

        private void Cancel()
        {
            if (_cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            _cancellationTokenSource.Cancel();
        }
    }
}
