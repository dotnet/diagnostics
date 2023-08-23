// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    [EventSource(Guid = "8C190136-52CE-4070-BBF1-2EF1E1368B5A", Name = "Microsoft.Diagnostics.Runtime.Windows.HeapSegmentCacheProvider")]
    internal sealed class HeapSegmentCacheEventSource : EventSource
    {
        internal static readonly HeapSegmentCacheEventSource Instance = new();

        /// <summary>
        /// The event ID for when the cache starts paging in data.
        /// </summary>
        private const int PageInDataEventStart = 1;

        /// <summary>
        /// The event ID for when the cache failed while paging in data.
        /// </summary>
        private const int PageInDataEventFailed = 2;

        /// <summary>
        /// The event ID for when the cache completes paging in data.
        /// </summary>
        private const int PageInDataEventEnd = 3;

        /// <summary>
        /// The event ID for when the cache starts paging out data.
        /// </summary>
        private const int PageOutDataEventStart = 4;

        /// <summary>
        /// The event ID for when the cache finishes paging out data.
        /// </summary>
        private const int PageOutDataEventEnd = 5;

        [Event(PageInDataEventStart, Task = Tasks.PageInData, Opcode = EventOpcode.Start, Level = EventLevel.Informational)]
        public void PageInDataStart(long requestedAddress, long dataSize)
        {
            this.WriteEvent(PageInDataEventStart, requestedAddress, dataSize);
        }

        [Event(PageInDataEventFailed, Task = Tasks.PageInData, Opcode = EventOpcode.Stop, Tags = Tags.Error, Level = EventLevel.Informational)]
        public void PageInDataFailed(string exceptionMessage)
        {
            this.WriteEvent(PageInDataEventFailed, exceptionMessage);
        }

        [Event(PageInDataEventEnd, Task = Tasks.PageInData, Opcode = EventOpcode.Stop, Tags = Tags.Success, Level = EventLevel.Informational)]
        public void PageInDataEnd(int dataRead)
        {
            this.WriteEvent(PageInDataEventEnd, dataRead);
        }

        [Event(PageOutDataEventStart, Task = Tasks.PageOutData, Opcode = EventOpcode.Start, Level = EventLevel.Informational)]
        public void PageOutDataStart()
        {
            this.WriteEvent(PageOutDataEventStart);
        }

        [Event(PageOutDataEventEnd, Task = Tasks.PageOutData, Opcode = EventOpcode.Stop, Tags = Tags.Success, Level = EventLevel.Informational)]
        public void PageOutDataEnd(long amountRemoved)
        {
            this.WriteEvent(PageOutDataEventEnd, amountRemoved);
        }

        /// <summary>
        /// Names of constants in this class make up the middle term in the event name
        /// E.g.: HeapSegmentCacheProvider/PageInData/Start.
        /// </summary>
        /// <remarks>Name of this class is important for EventSource.</remarks>
        public static class Tasks
        {
            public const EventTask PageInData = (EventTask)1;
            public const EventTask PageOutData = (EventTask)2;
        }

        /// <summary>
        /// Tags describing the event result
        /// </summary>
        /// <remarks>Name of this class is important for EventSource.</remarks>
        public static class Tags
        {
            public const EventTags Success = (EventTags)0x1;
            public const EventTags Error = (EventTags)0x2;
        }
    }
}
