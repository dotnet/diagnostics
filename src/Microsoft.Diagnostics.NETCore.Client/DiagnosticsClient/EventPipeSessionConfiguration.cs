// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal enum EventPipeSerializationFormat
    {
        NetPerf,
        NetTrace
    }

    public sealed class EventPipeSessionConfiguration
    {
        /// <summary>
        /// Creates a new configuration object for the EventPipeSession.
        /// For details, see the documentation of each property of this object.
        /// </summary>
        /// <param name="providers">An IEnumerable containing the list of Providers to turn on.</param>
        /// <param name="circularBufferSizeMB">The size of the runtime's buffer for collecting events in MB</param>
        /// <param name="requestRundown">If true, request rundown events from the runtime.</param>
        /// <param name="requestStackwalk">If true, record a stacktrace for every emitted event.</param>
        public EventPipeSessionConfiguration(
            IEnumerable<EventPipeProvider> providers,
            int circularBufferSizeMB = 256,
            bool requestRundown = true,
            bool requestStackwalk = true) : this(circularBufferSizeMB, EventPipeSerializationFormat.NetTrace, providers, requestRundown, requestStackwalk)
        {}

        private EventPipeSessionConfiguration(
            int circularBufferSizeMB,
            EventPipeSerializationFormat format,
            IEnumerable<EventPipeProvider> providers,
            bool requestRundown,
            bool requestStackwalk)
        {
            if (circularBufferSizeMB == 0)
            {
                throw new ArgumentException($"Buffer size cannot be zero.");
            }

            if (format is not EventPipeSerializationFormat.NetPerf and not EventPipeSerializationFormat.NetTrace)
            {
                throw new ArgumentException("Unrecognized format");
            }

            if (providers is null)
            {
                throw new ArgumentNullException(nameof(providers));
            };

            CircularBufferSizeInMB = circularBufferSizeMB;
            Format = format;
            RequestRundown = requestRundown;
            RequestStackwalk = requestStackwalk;
            _providers = new List<EventPipeProvider>(providers);
        }

        /// <summary>
        /// If true, request rundown events from the runtime.
        /// <list type="bullet">
        /// <item>Rundown events are needed to correctly decode the stacktrace information for dynamically generated methods.</item>
        /// <item>Rundown happens at the end of the session. It increases the time needed to finish the session and, for large applications, may have important impact on the final trace file size.</item>
        /// <item>Consider to set this parameter to false if you don't need stacktrace information or if you're analyzing events on the fly.</item>
        /// </list>
        /// </summary>
        public bool RequestRundown { get; }

        /// <summary>
        /// The size of the runtime's buffer for collecting events in MB.
        /// If the buffer size is too small to accommodate all in-flight events some events may be lost.
        /// </summary>
        public int CircularBufferSizeInMB { get; }

        /// <summary>
        /// If true, record a stacktrace for every emitted event.
        /// <list type="bullet">
        /// <item>The support of this parameter only comes with NET 9. Before, the stackwalk is always enabled and if this property is set to false the connection attempt will fail.</item>
        /// <item>Disabling the stackwalk makes event collection overhead considerably less</item>
        /// <item>Note that some events may choose to omit the stacktrace regardless of this parameter, specifically the events emitted from the native runtime code.</item>
        /// <item>If the stacktrace collection is disabled application-wide (using the env variable <c>DOTNET_EventPipeEnableStackwalk</c>) this parameter is ignored.</item>
        /// </list>
        /// </summary>
        public bool RequestStackwalk { get; }

        /// <summary>
        /// Providers to enable for this session.
        /// </summary>
        public IReadOnlyCollection<EventPipeProvider> Providers => _providers.AsReadOnly();

        private readonly List<EventPipeProvider> _providers;

        internal EventPipeSerializationFormat Format { get; }
    }

    internal static class EventPipeSessionConfigurationExtensions
    {
        public static byte[] SerializeV2(this EventPipeSessionConfiguration config)
        {
            byte[] serializedData = null;
            using (MemoryStream stream = new())
            using (BinaryWriter writer = new(stream))
            {
                writer.Write(config.CircularBufferSizeInMB);
                writer.Write((uint)config.Format);
                writer.Write(config.RequestRundown);

                SerializeProviders(config, writer);

                writer.Flush();
                serializedData = stream.ToArray();
            }

            return serializedData;
        }

        public static byte[] SerializeV3(this EventPipeSessionConfiguration config)
        {
            byte[] serializedData = null;
            using (MemoryStream stream = new())
            using (BinaryWriter writer = new(stream))
            {
                writer.Write(config.CircularBufferSizeInMB);
                writer.Write((uint)config.Format);
                writer.Write(config.RequestRundown);
                writer.Write(config.RequestStackwalk);

                SerializeProviders(config, writer);

                writer.Flush();
                serializedData = stream.ToArray();
            }

            return serializedData;
        }

        private static void SerializeProviders(EventPipeSessionConfiguration config, BinaryWriter writer)
        {
            writer.Write(config.Providers.Count);
            foreach (EventPipeProvider provider in config.Providers)
            {
                writer.Write(provider.Keywords);
                writer.Write((uint) provider.EventLevel);

                writer.WriteString(provider.Name);
                writer.WriteString(provider.GetArgumentString());
            }
        }
    }
}
