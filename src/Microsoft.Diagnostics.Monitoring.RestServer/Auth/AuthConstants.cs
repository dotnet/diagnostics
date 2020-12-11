// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    internal static class AuthConstants
    {
        public const string PolicyName = "AuthorizedUserPolicy";
        public const string NegotiateSchema = "Negotiate";
        public const string NtlmSchema = "NTLM";
        public const string KerberosSchema = "Kerberos";
        public const string ApiKeySchema = "MonitorApiKey";
    }
}
