// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal class AzureStorageOptions
    {
        public const string ConfigurationKey = "AzureStorage";

        public Dictionary<string, string> SasTokens { get; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
