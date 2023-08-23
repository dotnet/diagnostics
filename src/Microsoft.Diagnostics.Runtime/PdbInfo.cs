// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Information about a specific PDB instance obtained from a PE image.
    /// </summary>
    public sealed class PdbInfo :
#nullable disable // to enable use with both T and T? for reference types due to IEquatable<T> being invariant
        IEquatable<PdbInfo>
#nullable restore
    {
        /// <summary>
        /// Gets the Guid of the PDB.
        /// </summary>
        public Guid Guid { get; }

        /// <summary>
        /// Gets the PDB revision.
        /// </summary>
        public int Revision { get; }

        /// <summary>
        /// Gets the path to the PDB.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Creates an instance of the PdbInfo class with the corresponding properties initialized.
        /// </summary>
        public PdbInfo(string path, Guid guid, int rev)
        {
            Path = path;
            Guid = guid;
            Revision = rev;
        }

        /// <summary>
        /// GetHashCode implementation.
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return Guid.GetHashCode() ^ Revision;
        }

        /// <summary>
        /// Override for Equals.  Returns true if the guid, age, and file names equal.  Note that this compares only the.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns>True if the objects match, false otherwise.</returns>
        public override bool Equals(object? obj) => Equals(obj as PdbInfo);

        public bool Equals(PdbInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            if (Revision == other.Revision && Guid == other.Guid)
            {
                string thisFileName = System.IO.Path.GetFileName(Path);
                string otherFileName = System.IO.Path.GetFileName(other.Path);
                return thisFileName.Equals(otherFileName, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// To string implementation.
        /// </summary>
        /// <returns>Printing friendly version.</returns>
        public override string ToString()
        {
            return $"{Guid} {Revision} {Path}";
        }

        public static bool operator ==(PdbInfo? left, PdbInfo? right)
        {
            if (right is null)
                return left is null;

            return right.Equals(left);
        }

        public static bool operator !=(PdbInfo? left, PdbInfo? right) => !(left == right);
    }
}