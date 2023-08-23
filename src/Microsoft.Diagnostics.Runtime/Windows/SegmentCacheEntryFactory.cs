﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal abstract class SegmentCacheEntryFactory
    {
        public abstract SegmentCacheEntry CreateEntryForSegment(MinidumpSegment segmentData, Action<uint> updateOwningCacheForSizeChangeCallback);
    }
}