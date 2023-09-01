// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public class TimerInfo
    {
        public ulong TimerQueueTimerAddress { get; set; }
        public uint DueTime { get; set; }
        public uint Period { get; set; }
        public bool Cancelled { get; set; }
        public ulong StateAddress { get; set; }
        public string StateTypeName { get; set; }
        public string MethodName { get; set; }
        public bool? IsShort { get; set; }
    }
}
