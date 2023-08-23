// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Diagnostics.Runtime.DataReaders.Implementation
{
    /// <summary>
    /// This interface is implemented by all ClrMD provided implementations of <see cref="IDataReader"/>.
    /// This interface is not used by the ClrMD library itself, but is here to maintain functionality
    /// for previous uses of these functions in ClrMD 1.1's <see cref="IDataReader"/>.
    ///
    /// This inteface must always be requested and not assumed to be there:
    ///
    ///     IDataReader reader = ...;
    ///
    ///     if (reader is IThreadReader threadReader)
    ///         ...
    /// </summary>
    public interface IThreadReader
    {
        /// <summary>
        /// Enumerates the thread ids of all live threads in the target process.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<uint> EnumerateOSThreadIds();

        /// <summary>
        /// Obtains the Windows specific Thread Execution Block.
        /// </summary>
        /// <param name="osThreadId"></param>
        /// <returns></returns>
        public ulong GetThreadTeb(uint osThreadId);
    }
}