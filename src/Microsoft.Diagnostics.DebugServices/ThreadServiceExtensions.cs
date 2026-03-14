// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class ThreadServiceExtensions
    {
        /// <summary>
        /// Returns the register value for the thread context and register index. This function
        /// can only return register values that are 64 bits or less and currently the clrmd data
        /// targets don't return any floating point or larger registers.
        /// </summary>
        /// <param name="threadService">thread service instance</param>
        /// <param name="context">thread context</param>
        /// <param name="registerIndex">register index</param>
        /// <param name="value">value returned</param>
        /// <returns>true if value found</returns>
        public static bool TryGetRegisterValue(this IThreadService threadService, ReadOnlySpan<byte> context, int registerIndex, out ulong value)
        {
            if (threadService.TryGetRegisterInfo(registerIndex, out RegisterInfo info))
            {
                ReadOnlySpan<byte> threadSpan = context.Slice(info.RegisterOffset, info.RegisterSize);
                switch (info.RegisterSize)
                {
                    case 1:
                        value = MemoryMarshal.Read<byte>(threadSpan);
                        return true;
                    case 2:
                        value = MemoryMarshal.Read<ushort>(threadSpan);
                        return true;
                    case 4:
                        value = MemoryMarshal.Read<uint>(threadSpan);
                        return true;
                    case 8:
                        value = MemoryMarshal.Read<ulong>(threadSpan);
                        return true;
                    default:
                        Trace.TraceError($"GetRegisterValue: {info.RegisterName} invalid size {info.RegisterSize}");
                        break;
                }
            }
            value = 0;
            return false;
        }

        /// <summary>
        /// Change a specific register.
        /// </summary>
        /// <param name="threadService">thread service instance</param>
        /// <param name="context">writeable context span</param>
        /// <param name="registerIndex">register index</param>
        /// <param name="value">value to write</param>
        /// <returns></returns>
        public static bool TrySetRegisterValue(this IThreadService threadService, Span<byte> context, int registerIndex, ulong value)
        {
            if (threadService.TryGetRegisterInfo(registerIndex, out RegisterInfo info))
            {
                Span<byte> threadSpan = context.Slice(info.RegisterOffset, info.RegisterSize);
                switch (info.RegisterSize)
                {
                    case 1:
                        byte byteValue = (byte)value;
                        MemoryMarshal.Write<byte>(threadSpan, ref byteValue);
                        return true;
                    case 2:
                        ushort ushortValue = (ushort)value;
                        MemoryMarshal.Write<ushort>(threadSpan, ref ushortValue);
                        return true;
                    case 4:
                        uint uintValue = (uint)value;
                        MemoryMarshal.Write<uint>(threadSpan, ref uintValue);
                        return true;
                    case 8:
                        MemoryMarshal.Write<ulong>(threadSpan, ref value);
                        return true;
                    default:
                        Trace.TraceError($"SetRegisterValue: {info.RegisterName} invalid size {info.RegisterSize}");
                        break;
                }
            }
            return false;
        }
    }
}
