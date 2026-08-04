// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using Xunit;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// Tests for EventPipe IPC command selection (CollectTracing2..6) and the CollectTracing5/6 payload
    /// serialization, including per-provider event filters and the trailing buffering mode. This is
    /// wire-format logic that affects compatibility across runtime versions, so it is covered directly.
    /// </summary>
    public class EventPipeSessionConfigurationSerializationTests
    {
        private static List<EventPipeProvider> Providers(EventPipeProviderEventFilter filter = null) =>
            new() { new EventPipeProvider("My-Test-Source", EventLevel.Verbose, keywords: -1, arguments: null, eventFilter: filter) };

        private static byte CommandId(EventPipeSessionConfiguration config) =>
            EventPipeSession.CreateStartMessage(config).Header.CommandId;

        // ---- Command selection ----

        [Fact]
        public void Selects_CollectTracing2_ForDefaultConfig()
        {
            EventPipeSessionConfiguration config = new(Providers());
            Assert.Equal((byte)EventPipeCommandId.CollectTracing2, CommandId(config));
        }

        [Fact]
        public void Selects_CollectTracing3_WhenStackwalkDisabled()
        {
            EventPipeSessionConfiguration config = new(Providers(), circularBufferSizeMB: 256, requestRundown: true, requestStackwalk: false);
            Assert.Equal((byte)EventPipeCommandId.CollectTracing3, CommandId(config));
        }

        [Fact]
        public void Selects_CollectTracing4_ForCustomRundownKeyword()
        {
            EventPipeSessionConfiguration config = new(Providers(), circularBufferSizeMB: 256, rundownKeyword: 0x1);
            Assert.Equal((byte)EventPipeCommandId.CollectTracing4, CommandId(config));
        }

        [Fact]
        public void Selects_CollectTracing5_WhenProviderHasEventFilter()
        {
            EventPipeSessionConfiguration config = new(Providers(new EventPipeProviderEventFilter(enable: true, new uint[] { 1 })));
            Assert.Equal((byte)EventPipeCommandId.CollectTracing5, CommandId(config));
        }

        [Fact]
        public void Selects_CollectTracing6_ForBlockBufferingMode()
        {
            EventPipeSessionConfiguration config = new(Providers(), circularBufferSizeMB: 256, rundownKeyword: EventPipeSession.DefaultRundownKeyword, requestStackwalk: true, bufferingMode: EventPipeBufferingMode.Block);
            Assert.Equal((byte)EventPipeCommandId.CollectTracing6, CommandId(config));
        }

        [Fact]
        public void Selects_CollectTracing6_WhenBlockBufferingCombinedWithEventFilter()
        {
            EventPipeSessionConfiguration config = new(
                Providers(new EventPipeProviderEventFilter(enable: false, new uint[] { 2 })),
                circularBufferSizeMB: 256, rundownKeyword: EventPipeSession.DefaultRundownKeyword, requestStackwalk: true, bufferingMode: EventPipeBufferingMode.Block);
            Assert.Equal((byte)EventPipeCommandId.CollectTracing6, CommandId(config));
        }

        // ---- Payload serialization ----

        [Fact]
        public void SerializeV5_StreamingPayload_BeginsWithIpcStreamSessionType()
        {
            EventPipeSessionConfiguration config = new(Providers());
            byte[] payload = config.SerializeV5();
            // The CollectTracing5 streaming payload is prefixed with the session type; 0 == IpcStream.
            Assert.Equal(0u, BitConverter.ToUInt32(payload, 0));
        }

        [Fact]
        public void SerializeV6_IsV5PayloadPlusTrailingBufferingMode()
        {
            EventPipeSessionConfiguration config = new(Providers(), circularBufferSizeMB: 256, rundownKeyword: EventPipeSession.DefaultRundownKeyword, requestStackwalk: true, bufferingMode: EventPipeBufferingMode.Block);
            byte[] v5 = config.SerializeV5();
            byte[] v6 = config.SerializeV6();

            // V6 is the V5 streaming payload plus a trailing uint buffering mode.
            Assert.Equal(v5.Length + sizeof(uint), v6.Length);
            Assert.Equal(v5, v6.Take(v5.Length).ToArray());
            Assert.Equal((uint)EventPipeBufferingMode.Block, BitConverter.ToUInt32(v6, v5.Length));
        }

        [Fact]
        public void SerializeV5_WritesAllowListEventFilter()
        {
            uint[] ids = { 2, 4, 6 };
            EventPipeSessionConfiguration config = new(Providers(new EventPipeProviderEventFilter(enable: true, ids)));

            ParsedEventFilter filter = ParseProviderEventFilter(config.SerializeV5());

            Assert.True(filter.Enable);
            Assert.Equal(ids, filter.EventIds);
        }

        [Fact]
        public void SerializeV5_NullEventFilter_WritesAllowAll()
        {
            EventPipeSessionConfiguration config = new(Providers());

            ParsedEventFilter filter = ParseProviderEventFilter(config.SerializeV5());

            // "allow all" is encoded as a disabled, empty filter (enable=false, count=0).
            Assert.False(filter.Enable);
            Assert.Empty(filter.EventIds);
        }

        [Fact]
        public void OriginalEventPipeProviderConstructor_LeavesEventFilterNull()
        {
            // The original (binary-compatible) constructor must still exist and yield no filter.
            EventPipeProvider provider = new("My-Test-Source", EventLevel.Verbose);
            Assert.Null(provider.EventFilter);
        }

        // ---- helpers ----

        private struct ParsedEventFilter
        {
            public bool Enable;
            public uint[] EventIds;
        }

        // Parses the single provider out of a CollectTracing5 streaming payload, returning its event filter.
        private static ParsedEventFilter ParseProviderEventFilter(byte[] payload)
        {
            int i = 0;
            Assert.Equal(0u, BitConverter.ToUInt32(payload, i)); i += sizeof(uint);   // session_type (IpcStream)
            i += sizeof(int);    // circular buffer size MB
            i += sizeof(uint);   // format
            i += sizeof(long);   // rundown keyword
            i += sizeof(bool);   // request stackwalk

            int providerCount = BitConverter.ToInt32(payload, i); i += sizeof(int);
            Assert.Equal(1, providerCount);

            i += sizeof(ulong);  // keywords
            i += sizeof(uint);   // level
            IpcHelpers.ReadString(payload, ref i);   // name
            IpcHelpers.ReadString(payload, ref i);   // arguments

            ParsedEventFilter filter = default;
            filter.Enable = payload[i] != 0; i += sizeof(bool);
            uint count = BitConverter.ToUInt32(payload, i); i += sizeof(uint);
            filter.EventIds = new uint[count];
            for (int k = 0; k < count; k++)
            {
                filter.EventIds[k] = BitConverter.ToUInt32(payload, i); i += sizeof(uint);
            }

            return filter;
        }
    }
}
