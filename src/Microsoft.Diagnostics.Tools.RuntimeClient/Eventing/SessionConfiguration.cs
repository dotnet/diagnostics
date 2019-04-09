// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.RuntimeClient
{
    public struct SessionConfiguration
    {
        public SessionConfiguration(uint circularBufferSizeMB, string outputPath, IReadOnlyCollection<Provider> providers)
        {
            if (providers == null)
                throw new ArgumentNullException(nameof(providers));
            if (providers.Count() <= 0)
                throw new ArgumentException($"Specified providers collection is empty.");
            if (circularBufferSizeMB == 0)
                throw new ArgumentException($"Buffer size cannot be zero.");
            if (Directory.Exists(outputPath))
                throw new ArgumentException($"Specified output file name: {outputPath}, refers to a directory.");

            CircularBufferSizeInMB = circularBufferSizeMB;

            outputPath = outputPath ?? $"eventpipe-{DateTime.Now:yyyyMMdd_HHmmss}.netperf";
            outputPath = !outputPath.EndsWith(".netperf") ? $"{outputPath}.netperf" : outputPath;
            _outputPath = new FileInfo(fileName: outputPath);
            _providers = new List<Provider>(providers);
        }

        public uint CircularBufferSizeInMB { get; }

        public string OutputPath => _outputPath.FullName;

        public IReadOnlyCollection<Provider> Providers => _providers.AsReadOnly();

        private readonly FileInfo _outputPath;
        private readonly List<Provider> _providers;
    }
}
