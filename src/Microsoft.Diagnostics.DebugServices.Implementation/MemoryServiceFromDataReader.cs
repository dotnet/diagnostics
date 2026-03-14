// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Memory service implementation for the clrmd IDataReader
    /// </summary>
    public class MemoryServiceFromDataReader : IMemoryService
    {
        private readonly IDataReader _dataReader;

        /// <summary>
        /// Memory service constructor
        /// </summary>
        /// <param name="dataReader">CLRMD data reader</param>
        public MemoryServiceFromDataReader(IDataReader dataReader)
        {
            _dataReader = dataReader;
        }

        #region IMemoryService

        /// <summary>
        /// Returns the pointer size of the target
        /// </summary>
        public int PointerSize => _dataReader.PointerSize;

        /// <summary>
        /// Read memory out of the target process.
        /// </summary>
        /// <param name="address">The address of memory to read</param>
        /// <param name="buffer">The buffer to read memory into</param>
        /// <param name="bytesRead">The number of bytes actually read out of the target process</param>
        /// <returns>true if any bytes were read at all, false if the read failed (and no bytes were read)</returns>
        public bool ReadMemory(ulong address, Span<byte> buffer, out int bytesRead)
        {
            bytesRead = _dataReader.Read(address, buffer);
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
            bytesWritten = 0;
            return false;
        }

        #endregion
    }
}
