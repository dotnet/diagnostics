// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal static class DiagnosticsClientApiShimExtensions
    {
        // Generous timeout to allow APIs to respond on slower or more constrained machines
        private static readonly TimeSpan DefaultPositiveVerificationTimeout = TimeSpan.FromSeconds(30);

        public static Task<Dictionary<string, string>> GetProcessEnvironment(this DiagnosticsClientApiShim shim)
        {
            return shim.GetProcessEnvironment(DefaultPositiveVerificationTimeout);
        }

        public static Task<ProcessInfo> GetProcessInfo(this DiagnosticsClientApiShim shim)
        {
            return shim.GetProcessInfo(DefaultPositiveVerificationTimeout);
        }

        public static Task ResumeRuntime(this DiagnosticsClientApiShim shim)
        {
            return shim.ResumeRuntime(DefaultPositiveVerificationTimeout);
        }

        public static Task<EventPipeSession> StartEventPipeSession(this DiagnosticsClientApiShim shim, IEnumerable<EventPipeProvider> providers)
        {
            return shim.StartEventPipeSession(providers, DefaultPositiveVerificationTimeout);
        }

        public static Task<EventPipeSession> StartEventPipeSession(this DiagnosticsClientApiShim shim, EventPipeProvider provider)
        {
            return shim.StartEventPipeSession(provider, DefaultPositiveVerificationTimeout);
        }
    }
}
