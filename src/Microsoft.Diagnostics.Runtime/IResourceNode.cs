// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents a single resource node in a resource tree.
    /// </summary>
    public interface IResourceNode
    {
        /// <summary>
        /// The Children of this resource node.
        /// </summary>
        public ImmutableArray<IResourceNode> Children { get; }

        /// <summary>
        /// The name of this entry (may be null).
        /// </summary>
        public string? Name { get; }

        /// <summary>
        /// The size of the data carried by this resource node.
        /// </summary>
        public int Size { get; }

        /// <summary>
        /// Reads the data out of this resource node.
        /// </summary>
        /// <param name="buffer">The buffer to read the data into.</param>
        /// <param name="offset">The offset into the data to read.</param>
        /// <returns>The number of bytes read into buffer.</returns>
        public int Read(Span<byte> buffer, int offset);

        /// <summary>
        /// Reads the data out of this resource node into T.
        /// </summary>
        /// <typeparam name="T">An unmanaged struct to read the data into.</typeparam>
        /// <param name="offset">The offset into the data to read.</param>
        /// <returns>The data read, or <see langword="default"/> if we failed to read this data.</returns>
        public T Read<T>(int offset) where T : unmanaged;

        /// <summary>
        /// Returns the first child resource node that matches <paramref name="name"/>, or null if one doesn't exist.
        /// </summary>
        /// <param name="name">The name of the child node.</param>
        /// <returns>The matching resource node, or null if it doesn't exist.</returns>
        public IResourceNode? GetChild(string name);
    }
}