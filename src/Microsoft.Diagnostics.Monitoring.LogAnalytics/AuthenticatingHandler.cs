using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    internal sealed class AuthenticationDelegatingHandler : DelegatingHandler
    {
        //TODO Storing a high value bearer token in plain text in memory
        private AuthenticationHeaderValue _cachedBearerToken;

        public AuthenticationDelegatingHandler()
        {
        }

        public AuthenticationDelegatingHandler(HttpMessageHandler inner) : base(inner)
        {
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

            using HttpRequestMessage requestMessage = MetricsConfiguration.WithCredentials((MetricsConfiguration credentials) =>
            {
                Dictionary<string, string> formValues = new Dictionary<string, string>();
                formValues.Add("grant_type", "client_credentials");
                formValues.Add("client_id", credentials.ClientId);
                formValues.Add("client_id", credentials.ClientSecret);

                FormUrlEncodedContent content = new FormUrlEncodedContent(formValues);
                HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, FormattableString.Invariant($"https://login.microsoftonline.com/{credentials.TenantId}/oauth2/token"));

                return message;
            });

            HttpResponseMessage result = await httpclient.SendAsync(requestMessage, cancellationToken);

            AuthResult auth = await JsonSerializer.DeserializeAsync<AuthResult>(await result.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(), cancellationToken: cancellationToken);
            return new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        }
    }
}
