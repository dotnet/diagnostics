// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS.Extensions
{
    /// <summary>
    /// Memory service implementation using the native debugger services
    /// </summary>
    internal sealed class MemoryServiceFromDebuggerServices : IMemoryService
    {
        private readonly DebuggerServices _debuggerServices;

        /// <summary>
        /// Memory service constructor
        /// </summary>
        /// <param name="target">target instance</param>
        /// <param name="debuggerServices">native debugger services</param>
        internal MemoryServiceFromDebuggerServices(ITarget target, DebuggerServices debuggerServices)
        {
            Debug.Assert(target != null);
            Debug.Assert(debuggerServices != null);
            _debuggerServices = debuggerServices;

            switch (target.Architecture)
            {
                case Architecture.X64:
                case Architecture.Arm64:
                    PointerSize = 8;
                    break;
                case Architecture.X86:
                case Architecture.Arm:
                    PointerSize = 4;
                    break;
            }
        }

        #region IMemoryService

        /// <summary>
        /// Returns the pointer size of the target
        /// </summary>
        public int PointerSize { get; }

        /// <summary>
        /// Read memory out of the target process.
        /// </summary>
        /// <param name="address">The address of memory to read</param>
        /// <param name="buffer">The buffer to read memory into</param>
        /// <param name="bytesRead">The number of bytes actually read out of the target process</param>
        /// <returns>true if any bytes were read at all, false if the read failed (and no bytes were read)</returns>
        public bool ReadMemory(ulong address, Span<byte> buffer, out int bytesRead)
        {
            HResult hr = _debuggerServices.ReadVirtual(address, buffer, out bytesRead);
            if (hr != HResult.S_OK)
            {
                CheckCancellation(hr);

                bytesRead = 0;
            }
            return bytesRead > 0;
        }

        /// <summary>
        /// Write memory into target process for supported targets.
        /// </summary>
        /// <param name="address">The address of memory to write</param>
        /// <param name="buffer">The buffer to write</param>
        /// <param name="bytesWritten">The number of bytes successfully written</param>
        /// <returns>true if any bytes where written, false if write failed</returns>
        public bool WriteMemory(ulong address, Span<byte> buffer, out int bytesWritten)
        {
            HResult hr = _debuggerServices.WriteVirtual(address, buffer, out bytesWritten);
            if (hr != HResult.S_OK)
            {
                CheckCancellation(hr);

                bytesWritten = 0;
            }
            return bytesWritten > 0;
        }

        #endregion

        private static void CheckCancellation(HResult hr)
        {
            unchecked
            {
                if (hr == (int)0xd000013a)
                {
                    throw new OperationCanceledException();
                }
            }
        }
    }
}
