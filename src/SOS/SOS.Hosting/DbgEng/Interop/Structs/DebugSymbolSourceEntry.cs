// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct DEBUG_SYMBOL_SOURCE_ENTRY
    {
        private readonly ulong _moduleBase;
        private readonly ulong _offset;
        private readonly ulong _fileNameId;
        private readonly ulong _engineInternal;
        private readonly uint _size;
        private readonly uint _flags;
        private readonly uint _fileNameSize;
        // Line numbers are one-based.
        // May be DEBUG_ANY_ID if unknown.
        private readonly uint _startLine;
        private readonly uint _endLine;
        // Column numbers are one-based byte indices.
        // May be DEBUG_ANY_ID if unknown.
        private readonly uint _startColumn;
        private readonly uint _endColumn;
        private readonly uint _reserved;
    }
}
