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
