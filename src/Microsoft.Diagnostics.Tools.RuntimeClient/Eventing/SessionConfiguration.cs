// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.Eventing
{
    public struct SessionConfiguration
    {
        public SessionConfiguration(uint circularBufferSizeMB, ulong multiFileSec, string outputPath, IEnumerable<Provider> providers)
        {
            if (providers == null)
                throw new ArgumentNullException(nameof(providers));
            if (providers.Count() <= 0)
                throw new ArgumentException($"Specified providers collection is empty.");

            CircularBufferSizeInMB = circularBufferSizeMB;
            MultiFileTraceLengthInSeconds = multiFileSec;
            _outputPath = new FileInfo(fileName: outputPath ?? $"eventpipe-{DateTime.Now:yyyyMMdd_HHmmss}.netperf");
            _providers = new List<Provider>(providers);
        }

        public uint CircularBufferSizeInMB { get; }

        public ulong MultiFileTraceLengthInSeconds { get; }

        public string OutputPath => _outputPath.FullName;

        public IEnumerable<Provider> Providers => _providers;

        private readonly FileInfo _outputPath;
        private readonly List<Provider> _providers;
    }
}
