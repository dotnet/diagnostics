// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting
{
    /// <summary>
    /// Target architectures returned by ICorDebugDataTarget5.GetTargetInfo.
    /// </summary>
    public enum CorDebugTargetArchitecture
    {
        Unknown = 0,
        X86 = 1,
        Amd64 = 2,
        Arm = 3,
        Arm64 = 4,
        LoongArch64 = 5,
        RiscV64 = 6,
        Wasm = 7
    }

    /// <summary>
    /// Target OS.
    /// </summary>
    public enum CorDebugTargetOperatingSystem
    {
        Unknown = 0,
        Windows = 1,
        Linux = 2,
        OSX = 3
    }

    /// <summary>
    /// Native CorDebugTargetInfo layout: two 32-bit enums and a pointer size in bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CorDebugTargetInfo
    {
        public CorDebugTargetArchitecture Arch;
        public CorDebugTargetOperatingSystem OS;
        public uint PointerSize;
    }
}
