// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    internal sealed class AuthenticationDelegatingHandler : DelegatingHandler
    {
        //TODO Storing a high value bearer token in plain text in memory
        private AuthenticationHeaderValue _cachedBearerToken;
        private readonly MetricsConfiguration _metricsConfiguration;

        public AuthenticationDelegatingHandler(MetricsConfiguration configuration) : base(new HttpClientHandler())
        {
            _metricsConfiguration = configuration;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization == null)
            {
                if (_cachedBearerToken == null)
                {
                    _cachedBearerToken = await AcquireBearerToken(cancellationToken);
                }
                request.Headers.Authorization = _cachedBearerToken;
            }

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            //Possible Token expired
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _cachedBearerToken = await AcquireBearerToken(cancellationToken);
                request.Headers.Authorization = _cachedBearerToken;
                response = await base.SendAsync(request, cancellationToken);
            }
            return response;
        }

        private async Task<AuthenticationHeaderValue> AcquireBearerToken(CancellationToken cancellationToken)
        {
            using var httpclient = new HttpClient();

            var formValues = new Dictionary<string, string>();
            formValues.Add("grant_type", "client_credentials");
            formValues.Add("client_id", _metricsConfiguration.AadClientId);
            formValues.Add("client_secret", _metricsConfiguration.AadClientSecret);
            formValues.Add("resource", "https://monitoring.azure.com/");

            FormUrlEncodedContent content = new FormUrlEncodedContent(formValues);
            using HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, FormattableString.Invariant($"https://login.microsoftonline.com/{_metricsConfiguration.TenantId}/oauth2/token"));
            requestMessage.Content = content;

            using HttpResponseMessage result = await httpclient.SendAsync(requestMessage, cancellationToken);
            result.EnsureSuccessStatusCode();

            AuthResult auth = await JsonSerializer.DeserializeAsync<AuthResult>(await result.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(), cancellationToken: cancellationToken);
            return new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        }
    }
}
