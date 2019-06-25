// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.RuntimeClient
{
    public enum EventPipeSerializationFormat
    {
        NetPerf,
        NetTrace
    }

    public struct SessionConfiguration
    {
        public SessionConfiguration(uint circularBufferSizeMB, EventPipeSerializationFormat format, IReadOnlyCollection<Provider> providers)
        {
            if (circularBufferSizeMB == 0)
                throw new ArgumentException($"Buffer size cannot be zero.");
            if (format != EventPipeSerializationFormat.NetPerf && format != EventPipeSerializationFormat.NetTrace)
                throw new ArgumentException("Unrecognized format");
            if (providers == null)
                throw new ArgumentNullException(nameof(providers));
            if (providers.Count() <= 0)
                throw new ArgumentException($"Specified providers collection is empty.");

            CircularBufferSizeInMB = circularBufferSizeMB;
            Format = format;
            string extension = format == EventPipeSerializationFormat.NetPerf ? ".netperf" : ".nettrace";
            _providers = new List<Provider>(providers);
        }

        public uint CircularBufferSizeInMB { get; }
        public EventPipeSerializationFormat Format { get; }


        public IReadOnlyCollection<Provider> Providers => _providers.AsReadOnly();

        private readonly List<Provider> _providers;

        public byte[] Serialize()
        {
            byte[] serializedData = null;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(CircularBufferSizeInMB);
                writer.Write((uint)Format);

                writer.Write(Providers.Count());
                foreach (var provider in Providers)
                {
                    writer.Write(provider.Keywords);
                    writer.Write((uint)provider.EventLevel);

                    writer.WriteString(provider.Name);
                    writer.WriteString(provider.FilterData);
                }

                writer.Flush();
                serializedData = stream.ToArray();
            }

            return serializedData;
        }
    }
}
