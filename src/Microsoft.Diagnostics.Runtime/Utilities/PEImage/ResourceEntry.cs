// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// An entry in the resource table.
    /// </summary>
    internal sealed class ResourceEntry : IResourceNode
    {
        private ImmutableArray<IResourceNode> _children;
        private readonly int _offset;

        /// <summary>
        /// The maximum number of children nodes that ResourceEntry objects will consider.  Note that if a PEImage is
        /// corrupted or if we read bad data out of the target then we may misinterpret the data we read and spend
        /// a lot of time enumerating bad resources.  Setting this to int.MaxValue removes this limitation.
        /// </summary>
        public static int MaxChildrenCount { get; set; } = 128;

        /// <summary>
        /// The maximum length ResourceEntry.Name strings we will return.  Note that if a PEImage is
        /// corrupted or if we read bad data out of the target then we may misinterpret the data we read and spend
        /// a lot of time enumerating bad resources.  Setting this to int.MaxValue removes this limitation.
        /// </summary>
        public static int MaxNameLength { get; set; } = 512;

        /// <summary>
        /// Gets the PEImage containing this ResourceEntry.
        /// </summary>
        public PEImage Image { get; }

        /// <summary>
        /// Gets the parent resource of this ResourceEntry.  Null if and only if this is the root node.
        /// </summary>
        public ResourceEntry? Parent { get; }

        /// <summary>
        /// Gets resource Name.  May be <see langword="null"/> if this is the root node.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets a value indicating whether this is a leaf, and contains data.
        /// </summary>
        public bool IsLeaf { get; }

        /// <summary>
        /// Gets the size of data for this node.
        /// </summary>
        public int Size
        {
            get
            {
                GetDataVaAndSize(out _, out int size);
                return size;
            }
        }

        /// <summary>
        /// Gets the number of children this entry contains.  Note that ResourceEntry.Children is capped at
        /// MaxChildrenCount entries.  This property returns the total number of entries as defined by the
        /// IMAGE_RESOURCE_DIRECTORY.  That means this number may be larger than Children.Count.
        /// </summary>
        public int ChildCount
        {
            get
            {
                (_, IMAGE_RESOURCE_DIRECTORY hdr) = GetHeader();
                return hdr.NumberOfNamedEntries + hdr.NumberOfIdEntries;
            }
        }

        /// <summary>
        /// Returns the given resource child by name.
        /// </summary>
        /// <param name="name">The name of the child to return.</param>
        /// <returns>The child in question, or <see langword="null"/> if none are found with that name.</returns>
        public IResourceNode? GetChild(string name) => Children.FirstOrDefault(c => c.Name == name);

        internal ResourceEntry(PEImage image, ResourceEntry? parent, string name, bool leaf, int offset)
        {
            Image = image;
            Parent = parent;
            Name = name;
            IsLeaf = leaf;
            _offset = offset;
        }

        /// <summary>
        /// The data associated with this entry.
        /// </summary>
        /// <returns>A byte array of the data, or a byte[] of length 0 if this entry contains no data.</returns>
        public int Read(Span<byte> span, int offset)
        {
            GetDataVaAndSize(out int va, out int size);
            if (size == 0 || va == 0)
                return 0;

            return Image.Read(va + offset, span);
        }

        /// <summary>
        /// A convenience function to get structured data out of this entry.
        /// </summary>
        /// <typeparam name="T">A struct type to convert.</typeparam>
        /// <param name="offset">The offset into the data.</param>
        /// <returns>The struct that was read out of the data section.</returns>
        public unsafe T Read<T>(int offset) where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            GetDataVaAndSize(out int va, out int sectionSize);
            if (va == 0 || sectionSize < size + offset)
                return default;

            T output;
            int read = Image.Read(va + offset, new Span<byte>(&output, size));
            return read == size ? output : default;
        }

        /// <summary>
        /// Gets the children resources of this ResourceEntry.
        /// </summary>
        public ImmutableArray<IResourceNode> Children
        {
            get
            {
                if (!_children.IsDefault)
                    return _children;

                if (IsLeaf)
                    return _children = ImmutableArray<IResourceNode>.Empty;

                try
                {
                    (int offset, IMAGE_RESOURCE_DIRECTORY hdr) = GetHeader();
                    ResourceEntry root = Image.Resources;
                    int resourceStartFileOffset = root._offset;

                    // Cap the number of entires we inspect
                    int count = Math.Min(hdr.NumberOfNamedEntries + hdr.NumberOfIdEntries, MaxChildrenCount);

                    ImmutableArray<IResourceNode>.Builder result = ImmutableArray.CreateBuilder<IResourceNode>(count);

                    for (int i = 0; i < count; i++)
                    {
                        if (!Image.TryRead<ImageResourceDirectoryEntry>(ref offset, out ImageResourceDirectoryEntry entry))
                            break;

                        string name;
                        if (!entry.IsStringName)
                            name = ImageResourceDirectoryEntry.GetTypeNameForTypeId(entry.Id);
                        else
                            name = GetName(entry.NameOffset, resourceStartFileOffset);

                        result.Add(new ResourceEntry(Image, this, name, entry.IsLeaf, resourceStartFileOffset + entry.DataOffset));
                    }

                    return _children = result.MoveOrCopyToImmutable();
                }
                catch
                {
                    // If there's a bad image we could hit a variety of different failures here, including out of memory or
                    // under/overflow issues.  We'll just not return anything if we hit an error here since a bad image
                    // could lead to really unpredictable behavior if we are interpreting random bits of data.
                    return _children = ImmutableArray<IResourceNode>.Empty;
                }
            }
        }

        private (int OffsetAfterHeader, IMAGE_RESOURCE_DIRECTORY Header) GetHeader()
        {
            int offset = _offset;

            if (Image.TryRead<IMAGE_RESOURCE_DIRECTORY>(ref offset, out IMAGE_RESOURCE_DIRECTORY hdr))
                return (offset, hdr);

            // Returning default will mean that our count will == 0, so we don't have to turn this
            // into "bool TryGetHeader".
            return (offset, default);
        }

        private string GetName(int nameOffset, int resourceStartFileOffset)
        {
            int offset = resourceStartFileOffset + nameOffset;
            int len = Image.Read<ushort>(ref offset);

            // Cap the length of the string we will read
            len = Math.Min(len, MaxNameLength);
            if (len == 0)
                return string.Empty;

            char[] buffer = ArrayPool<char>.Shared.Rent(len);
            try
            {
                Span<char> span = new(buffer, 0, len);
                int count = Image.ReadFromOffset(offset, MemoryMarshal.AsBytes(span)) >> 1;

                int i = 0;
                while (i < len && buffer[i] != 0)
                    i++;

                return new string(buffer, 0, i);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        private void GetDataVaAndSize(out int va, out int size)
        {
            ImageResourceDataEntry dataEntry = Image.Read<ImageResourceDataEntry>(_offset);
            va = dataEntry.RvaToData;
            size = dataEntry.Size;
        }

        public override string ToString() => Name;
    }
}
