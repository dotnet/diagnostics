// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    /// <summary>
    /// Authenticates against the ApiKey stored on the server.
    /// </summary>
    internal sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationHandlerOptions>
    {
        private static readonly string[] DisallowedHashAlgorithms = new string[]
        {
            "SHA", "SHA1", "System.Security.Cryptography.SHA1", "System.Security.Cryptography.HashAlgorithm", "MD5", "System.Security.Cryptography.MD5"
        };

        public ApiKeyAuthenticationHandler(IOptionsMonitor<ApiKeyAuthenticationHandlerOptions> options, ILoggerFactory loggerFactory, UrlEncoder encoder, ISystemClock clock)
            : base(options, loggerFactory, encoder, clock)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            //We are expecting a header such as Authorization: <Schema> <key>
            //If this is not present, we will return NoResult and move on to the next authentication handler.
            if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out StringValues values) ||
                !values.Any())
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            if (!AuthenticationHeaderValue.TryParse(values.First(), out AuthenticationHeaderValue authHeader))
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid authentication header"));
            }

            if (!string.Equals(authHeader.Scheme, Scheme.Name, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            //The user is passing a base 64-encoded version of the secret
            //We will be hash this and compare it to the secret in our configuration.
            byte[] secret = new byte[32];
            Span<byte> span = new Span<byte>(secret);
            if (!Convert.TryFromBase64String(authHeader.Parameter, span, out int bytesWritten) || bytesWritten < 32)
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid Api Key format"));
            }

            //HACK IOptionsMonitor and similiar do not properly update here even though the underlying
            //configuration is updated. We get the value directly from IConfiguration.

            var authenticationOptions = new ApiAuthenticationOptions();
            IConfiguration configService = Context.RequestServices.GetRequiredService<IConfiguration>();
            configService.Bind(ApiAuthenticationOptions.ConfigurationKey, authenticationOptions);
            string apiKeyHash = authenticationOptions.ApiKeyHash;
            if (apiKeyHash == null)
            {
                return Task.FromResult(AuthenticateResult.Fail("Server does not contain Api Key"));
            }
            if (string.IsNullOrEmpty(authenticationOptions.ApiKeyHashType))
            {
                return Task.FromResult(AuthenticateResult.Fail("Missing hash algorithm"));
            }
            if (DisallowedHashAlgorithms.Contains(authenticationOptions.ApiKeyHashType, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.Fail($"Disallowed hash algorithm {authenticationOptions.ApiKeyHashType}"));
            }

            using HashAlgorithm algorithm = HashAlgorithm.Create(authenticationOptions.ApiKeyHashType);
            if (algorithm == null)
            {
                return Task.FromResult(AuthenticateResult.Fail($"Invalid hash algorithm {authenticationOptions.ApiKeyHashType}"));
            }

            byte[] hashedSecret = algorithm.ComputeHash(secret);

            //ApiKeyHash is represented as a hex string. e.g. AABBCCDDEEFF
            byte[] apiKeyHashBytes = new byte[apiKeyHash.Length / 2];
            for (int i = 0; i < apiKeyHash.Length; i += 2)
            {
                if (!byte.TryParse(apiKeyHash.AsSpan(i, 2), NumberStyles.HexNumber, provider: NumberFormatInfo.InvariantInfo, result: out byte resultByte))
                {
                    return Task.FromResult(AuthenticateResult.Fail("Invalid Api Key hash"));
                }
                apiKeyHashBytes[i / 2] = resultByte;
            }

            if (hashedSecret.SequenceEqual(apiKeyHashBytes))
            {
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(
                        new ClaimsPrincipal(new[] { new ClaimsIdentity(AuthConstants.ApiKeySchema) }),
                        AuthConstants.ApiKeySchema)));

            }
            else
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid Api Key"));
            }
        }
    }
}
