// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Represents the version of a module
    /// </summary>
    public sealed class VersionData : IEquatable<VersionData>, IComparable<VersionData>
    {
        /// <summary>
        /// In a version 'A.B.C.D', this field represents 'A'.
        /// </summary>
        public int Major { get; }

        /// <summary>
        /// In a version 'A.B.C.D', this field represents 'B'.
        /// </summary>
        public int Minor { get; }

        /// <summary>
        /// In a version 'A.B.C.D', this field represents 'C'.
        /// </summary>
        public int Revision { get; }

        /// <summary>
        /// In a version 'A.B.C.D', this field represents 'D'.
        /// </summary>
        public int Patch { get; }

        public VersionData(int major, int minor, int revision, int patch)
        {
            if (major < 0)
                throw new ArgumentOutOfRangeException(nameof(major));

            if (minor < 0)
                throw new ArgumentOutOfRangeException(nameof(minor));

            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));

            if (patch < 0)
                throw new ArgumentOutOfRangeException(nameof(patch));

            Major = major;
            Minor = minor;
            Revision = revision;
            Patch = patch;
        }

        /// <inheritdoc/>
        public bool Equals(VersionData other) => Major == other.Major && Minor == other.Minor && Revision == other.Revision && Patch == other.Patch;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is VersionData other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Major;
                hashCode = (hashCode * 397) ^ Minor;
                hashCode = (hashCode * 397) ^ Revision;
                hashCode = (hashCode * 397) ^ Patch;
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public int CompareTo(VersionData other)
        {
            if (Major != other.Major)
                return Major.CompareTo(other.Major);

            if (Minor != other.Minor)
                return Minor.CompareTo(other.Minor);

            if (Revision != other.Revision)
                return Revision.CompareTo(other.Revision);

            return Patch.CompareTo(other.Patch);
        }

        public override string ToString() => $"{Major}.{Minor}.{Revision}.{Patch}";

        public static bool operator ==(VersionData left, VersionData right) => left.Equals(right);

        public static bool operator !=(VersionData left, VersionData right) => !(left == right);

        public static bool operator <(VersionData left, VersionData right) => left.CompareTo(right) < 0;

        public static bool operator <=(VersionData left, VersionData right) => left.CompareTo(right) <= 0;

        public static bool operator >(VersionData left, VersionData right) => right < left;

        public static bool operator >=(VersionData left, VersionData right) => right <= left;
    }
}