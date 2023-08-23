// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// A helper class to represent an ELF note section.
    /// </summary>
    internal sealed class ElfNote
    {
        private readonly Reader _reader;
        private readonly ulong _position;
        private string? _name;

        internal ElfNoteHeader Header { get; }

        /// <summary>
        /// The content size of the data stored within this note.
        /// </summary>
        public int ContentSize => Header.ContentSize > int.MaxValue ? int.MaxValue : (int)Header.ContentSize;

        /// <summary>
        /// The type of note this is.
        /// </summary>
        public ElfNoteType Type => Header.Type;

        /// <summary>
        /// The note's name.
        /// </summary>
        public string Name
        {
            get
            {
                if (_name != null)
                    return _name;

                ulong namePosition = _position + HeaderSize;
                _name = _reader.ReadNullTerminatedAscii(namePosition, (int)Header.NameSize);
                return _name;
            }
        }

        internal ulong TotalSize => HeaderSize + Align4(Header.NameSize) + Align4(Header.ContentSize);

        private static unsafe uint HeaderSize => (uint)sizeof(ElfNoteHeader);

        /// <summary>
        /// Reads the contents of this note file.
        /// </summary>
        /// <param name="position">The position within the note to read from.</param>
        /// <param name="buffer">The buffer to read the note into.</param>
        /// <returns>The number of bytes read written to buffer.</returns>
        public int ReadContents(ulong position, Span<byte> buffer)
        {
            ulong contentsoffset = _position + HeaderSize + Align4(Header.NameSize);
            return _reader.ReadBytes(position + contentsoffset, buffer);
        }

        internal T ReadContents<T>(ref ulong position)
            where T : unmanaged
        {
            ulong contentsOffset = _position + HeaderSize + Align4(Header.NameSize);
            ulong locationOrig = contentsOffset + position;
            ulong location = locationOrig;
            T result = _reader.Read<T>(ref location);

            position += location - locationOrig;
            return result;
        }

        /// <summary>
        /// Reads the contents of this note file.
        /// </summary>
        /// <param name="position">The position within the note to read from.</param>
        /// <exception cref="System.IO.IOException">If the data could not be read.</exception>
        /// <returns>The data at the given position.</returns>
        public T ReadContents<T>(ulong position)
            where T : unmanaged
        {
            ulong contentsOffset = _position + HeaderSize + Align4(Header.NameSize);
            ulong location = contentsOffset + position;
            T result = _reader.Read<T>(location);
            return result;
        }

        internal ElfNote(Reader reader, ulong position)
        {
            _position = position;
            _reader = reader;

            Header = _reader.Read<ElfNoteHeader>(_position);
        }

        private static uint Align4(uint x)
        {
            return (x + 3U) & ~3U;
        }
    }
}
