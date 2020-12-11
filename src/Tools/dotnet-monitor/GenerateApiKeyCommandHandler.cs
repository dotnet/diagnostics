// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    /// <summary>
    /// Used to generate Api Key for authentication. The first output is
    /// part of the Authorization header, and is the Base64 encoded key.
    /// The second output is a hex encoded string of the hash of the secret.
    /// </summary>
    internal sealed class GenerateApiKeyCommandHandler
    {
        public Task<int> GenerateApiKey(CancellationToken token, IConsole console)
        {
            using RandomNumberGenerator rng = RandomNumberGenerator.Create();
            using HashAlgorithm hashAlgorithm = SHA256.Create();

            byte[] secret = new byte[32];
            rng.GetBytes(secret);

            byte[] hash = hashAlgorithm.ComputeHash(secret);

            Console.Out.WriteLine(FormattableString.Invariant($"Authorization: {Monitoring.RestServer.AuthConstants.ApiKeySchema} {Convert.ToBase64String(secret)}"));
            Console.Out.Write("HashedSecret ");
            foreach (byte b in hash)
            {
                console.Out.Write(b.ToString("X2"));
            }
            console.Out.WriteLine();

            return Task.FromResult(0);
        }
    }
}
