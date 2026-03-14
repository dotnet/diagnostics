// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    /// <summary>
    /// Parses a string object from a standard null-terminated byte sequence.
    /// </summary>
    /// <remarks>
    /// Currently this type only supports ASCII or UTF8 encoding.
    /// </remarks>
    public class NullTerminatedStringLayout : ILayout
    {
        private Encoding _encoding;

        /// <summary>
        /// Create a new NullTerminatedStringLayout
        /// </summary>
        /// <param name="encoding">The encoding used to parse the string characters. Currently on ASCII or UTF8 is supported</param>
        public NullTerminatedStringLayout(Encoding encoding)
        {
            // Make sure we are scanning something where a single 0 byte means end of string.
            // Although UTF8 does have code points that encode with more than one byte,
            // byte 0 is never used except to encode code point 0.
            if (encoding != Encoding.UTF8 && encoding != Encoding.ASCII)
            {
                throw new NotSupportedException("PEReader.ReadNullTerminatedString: Only UTF8 or ascii are supported for now");
            }
            _encoding = encoding;

            // We could implement this for multi-byte encodings, there hasn't been a
            // need yet. If you do change it, make sure to adjust NaturalAlignment too
        }

        public IEnumerable<IField> Fields { get { return Array.Empty<IField>(); } }
        public uint NaturalAlignment { get { return 1U; } }

        public bool IsFixedSize { get { return false; } }

        public uint Size
        {
            get
            {
                throw new InvalidOperationException("Size is invalid for variable sized layouts");
            }
        }

        public uint SizeAsBaseType
        {
            get
            {
                throw new InvalidOperationException("Size is invalid for variable sized layouts");
            }
        }

        public Type Type { get { return typeof(string); } }

        public object Read(IAddressSpace dataSource, ulong position)
        {
            return Read(dataSource, position, out uint _);
        }

        public object Read(IAddressSpace dataSource, ulong position, out uint bytesRead)
        {
            List<byte> stringBytes = new();
            uint offset = 0;
            for (; ; offset++)
            {
                byte[] nextByte = dataSource.Read(position + offset, 1);
                if (nextByte[0] == 0)
                {
                    break;
                }
                else
                {
                    stringBytes.Add(nextByte[0]);
                }
            }
            bytesRead = offset + 1;
            return _encoding.GetString(stringBytes.ToArray(), 0, stringBytes.Count);
        }
    }

    public static partial class LayoutManagerExtensions
    {
        /// <summary>
        /// Add support for parsing null terminated strings as System.String
        /// </summary>
        public static LayoutManager AddNullTerminatedString(this LayoutManager layouts)
        {
            return AddNullTerminatedString(layouts, Encoding.UTF8);
        }

        /// <summary>
        /// Add support for parsing null terminated strings as System.String
        /// </summary>
        /// <param name="layouts">The layout manager that will hold the new layout</param>
        /// <param name="encoding">The encoding used to parse string characters. Currently only UTF8 and ASCII are supported</param>
        public static LayoutManager AddNullTerminatedString(this LayoutManager layouts, Encoding encoding)
        {
            layouts.AddLayout(new NullTerminatedStringLayout(encoding));
            return layouts;
        }
    }
}
