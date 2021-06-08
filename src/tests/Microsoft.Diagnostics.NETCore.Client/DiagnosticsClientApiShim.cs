// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// Unifies the async and non-async methods of the DiagnosticsClient class
    /// so that tests do not need to be duplicated for testing each version of the
    /// same API.
    /// </summary>
    internal sealed class DiagnosticsClientApiShim
    {
        private readonly DiagnosticsClient _client;
        private readonly bool _useAsync;

        public DiagnosticsClientApiShim(DiagnosticsClient client, bool useAsync)
        {
            _client = client;
            _useAsync = useAsync;
        }

        public async Task<Dictionary<string, string>> GetProcessEnvironment(TimeSpan timeout)
        {
            if (_useAsync)
            {
                using CancellationTokenSource cancellation = new CancellationTokenSource(timeout);
                return await _client.GetProcessEnvironmentAsync(cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                return _client.GetProcessEnvironment();
            }
        }

        public async Task<ProcessInfo> GetProcessInfo(TimeSpan timeout)
        {
            if (_useAsync)
            {
                using CancellationTokenSource cancellation = new CancellationTokenSource(timeout);
                return await _client.GetProcessInfoAsync(cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                return _client.GetProcessInfo();
            }
        }

        public async Task ResumeRuntime(TimeSpan timeout)
        {
            if (_useAsync)
            {
                using CancellationTokenSource cancellation = new CancellationTokenSource(timeout);
                await _client.ResumeRuntimeAsync(cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                _client.ResumeRuntime();
            }
        }

        public async Task<EventPipeSession> StartEventPipeSession(IEnumerable<EventPipeProvider> providers, TimeSpan timeout)
        {
            if (_useAsync)
            {
                CancellationTokenSource cancellation = new CancellationTokenSource(timeout);
                return await _client.StartEventPipeSessionAsync(providers, true, circularBufferMB: 256, cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                return _client.StartEventPipeSession(providers);
            }
        }

        public async Task<EventPipeSession> StartEventPipeSession(EventPipeProvider provider, TimeSpan timeout)
        {
            if (_useAsync)
            {
                CancellationTokenSource cancellation = new CancellationTokenSource(timeout);
                return await _client.StartEventPipeSessionAsync(provider, true, circularBufferMB: 256, cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                return _client.StartEventPipeSession(provider);
            }
        }
    }
}
