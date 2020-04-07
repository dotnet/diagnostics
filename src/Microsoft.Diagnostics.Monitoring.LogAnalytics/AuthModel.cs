// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    internal sealed class AuthResult
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }
    }
}
