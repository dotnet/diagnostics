// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct ElfPRStatusArm : IElfPRStatus
    {
        public ElfSignalInfo SignalInfo;
        public short CurrentSignal;
        private readonly ushort Padding;
        public uint SignalsPending;
        public uint SignalsHeld;

        public uint Pid;
        public uint PPid;
        public uint PGrp;
        public uint Sid;

        public TimeVal32 UserTime;
        public TimeVal32 SystemTime;
        public TimeVal32 CUserTime;
        public TimeVal32 CSystemTime;

        public RegSetArm RegisterSet;

        public int FPValid;

        public uint ProcessId => PGrp;

        public uint ThreadId => Pid;

        public bool CopyRegistersAsContext(Span<byte> context) => RegisterSet.CopyContext(context);
    }
}
