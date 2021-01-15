﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting
{
    /// <summary>
    /// Represents a version of the CLR runtime
    /// </summary>
    public struct ClrDebuggingVersion
    {
        public short StructVersion;
        public short Major;
        public short Minor;
        public short Build;
        public short Revision;
    }
}