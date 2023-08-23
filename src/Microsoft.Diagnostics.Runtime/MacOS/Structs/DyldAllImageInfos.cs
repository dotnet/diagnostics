// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.MacOS.Structs
{
    internal readonly struct DyldAllImageInfos
    {
        public uint version { get; }
        public uint infoArrayCount { get; }
        public UIntPtr infoArray { get; }
        public IntPtr notification { get; }
        public bool processDetachedFromSharedRegion { get; }
        public bool libSystemInitialized { get; }
        public IntPtr dyldImageLoadAddress { get; }
        public IntPtr jitInfo { get; }
        public IntPtr dyldVersion { get; }
        public IntPtr errorMessage { get; }
        public IntPtr terminationFlags { get; }
        public IntPtr coreSymbolicationShmPage { get; }
        public IntPtr systemOrderFlag { get; }
        public IntPtr uuidArrayCount { get; }
        public IntPtr uuidArray { get; }
        public IntPtr dyldAllImageInfosAddress { get; }
        public IntPtr initialImageCount { get; }
        public IntPtr errorKind { get; }
        public IntPtr errorClientOfDylibPath { get; }
        public IntPtr errorTargetDylibPath { get; }
        public IntPtr errorSymbol { get; }
        public IntPtr sharedCacheSlide { get; }
    }
}