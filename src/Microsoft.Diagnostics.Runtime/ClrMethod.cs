// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents a method on a class.
    /// </summary>
    public sealed class ClrMethod :
#nullable disable // to enable use with both T and T? for reference types due to IEquatable<T> being invariant
        IEquatable<ClrMethod>, IClrMethod
#nullable restore
    {
        private readonly IClrMethodHelpers _helpers;
        private string? _signature;
        private MethodAttributes? _attributes;

        internal ClrMethod(IClrMethodHelpers helpers, ClrType type, ulong md, int token, MethodCompilationType compilationType, in HotColdRegions regions)
        {
            _helpers = helpers;
            Type = type;
            MethodDesc = md;
            MetadataToken = token;
            HotColdInfo = regions;
            CompilationType = compilationType;
        }

        /// <summary>
        /// Gets the first MethodDesc in EnumerateMethodDescs().  For single
        /// AppDomain programs this is the only MethodDesc.  MethodDescs
        /// are unique to an Method/AppDomain pair, so when there are multiple domains
        /// there will be multiple MethodDescs for a method.
        /// </summary>
        public ulong MethodDesc { get; }

        /// <summary>
        /// Gets the name of the method.  For example, "void System.Foo.Bar(object o, int i)" would return "Bar".
        /// </summary>
        public string? Name
        {
            get
            {
                string? signature = Signature;
                if (signature is null)
                    return null;

                int last = signature.LastIndexOf('(');
                if (last > 0)
                {
                    int first = signature.LastIndexOf('.', last - 1);

                    if (first != -1 && signature[first - 1] == '.')
                        first--;

                    return signature.Substring(first + 1, last - first - 1);
                }

                return "{error}";
            }
        }

        /// <summary>
        /// Gets the full signature of the function.  For example, "void System.Foo.Bar(object o, int i)"
        /// would return "System.Foo.Bar(System.Object, System.Int32)"
        /// </summary>
        public string? Signature
        {
            get
            {
                if (_signature != null)
                    return _signature;

                // returns whether we should cache the signature or not.
                if (_helpers.GetSignature(MethodDesc, out string? signature))
                    _signature = signature;

                return signature;
            }
        }

        /// <summary>
        /// Gets the instruction pointer in the target process for the start of the method's assembly.
        /// </summary>
        public ulong NativeCode => HotColdInfo.HotStart != 0 ? HotColdInfo.HotStart : HotColdInfo.ColdStart;

        /// <summary>
        /// Gets the ILOffset of the given address within this method.
        /// </summary>
        /// <param name="addr">The absolute address of the code (not a relative offset).</param>
        /// <returns>The IL offset of the given address.</returns>
        public int GetILOffset(ulong addr)
        {
            ImmutableArray<ILToNativeMap> map = ILOffsetMap;
            if (map.IsDefault)
                return 0;

            int ilOffset = 0;
            if (map.Length > 1)
                ilOffset = map[1].ILOffset;

            for (int i = 0; i < map.Length; ++i)
                if (map[i].StartAddress <= addr && addr <= map[i].EndAddress)
                    return map[i].ILOffset;

            return ilOffset;
        }

        /// <summary>
        /// Gets the location in memory of the IL for this method.
        /// </summary>
        public ILInfo? GetILInfo()
        {
            IDataReader dataReader = _helpers.DataReader;
            ClrModule? module = Type.Module;
            if (module is null)
                return null;

            MetadataImport? mdImport = module.MetadataImport;
            if (mdImport is null)
                return null;

            uint rva = mdImport.GetRva(MetadataToken);

            ulong il = _helpers.GetILForModule(module.Address, rva);
            if (il != 0)
            {
                if (dataReader.Read(il, out byte b))
                {
                    bool isTinyHeader = (b & (IMAGE_COR_ILMETHOD.FormatMask >> 1)) == IMAGE_COR_ILMETHOD.TinyFormat;
                    if (isTinyHeader)
                    {
                        ulong address = il + 1;
                        int len = b >> (int)(IMAGE_COR_ILMETHOD.FormatShift - 1);
                        uint localToken = IMAGE_COR_ILMETHOD.mdSignatureNil;

                        return new ILInfo(address, len, 0, localToken);
                    }
                    else if (dataReader.Read(il, out uint flags))
                    {
                        int len = dataReader.Read<int>(il + 4);
                        uint localToken = dataReader.Read<uint>(il + 8);
                        ulong address = il + 12;

                        return new ILInfo(address, len, flags, localToken);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the regions of memory that
        /// </summary>
        public HotColdRegions HotColdInfo { get; }

        /// <summary>
        /// Gets the way this method was compiled.
        /// </summary>
        public MethodCompilationType CompilationType { get; }

        /// <summary>
        /// Gets the IL to native offset mapping.
        /// </summary>
        public ImmutableArray<ILToNativeMap> ILOffsetMap => _helpers.GetILMap(this);

        /// <summary>
        /// Gets the metadata token of the current method.
        /// </summary>
        public int MetadataToken { get; }

        /// <summary>
        /// Gets the enclosing type of this method.
        /// </summary>
        public ClrType Type { get; }

        IClrType IClrMethod.Type => Type;

        public MethodAttributes Attributes
        {
            get
            {
                if (!_attributes.HasValue)
                    _attributes = Type.Module?.MetadataImport?.GetMethodAttributes(MetadataToken) ?? default;

                return _attributes.Value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this method is an instance constructor.
        /// </summary>
        public bool IsConstructor => Name == ".ctor";

        /// <summary>
        /// Gets a value indicating whether this method is a static constructor.
        /// </summary>
        public bool IsClassConstructor => Name == ".cctor";

        public override bool Equals(object? obj) => Equals(obj as ClrMethod);

        public bool Equals(ClrMethod? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            if (MethodDesc == other.MethodDesc)
                return true;

            // MethodDesc shouldn't be 0, but we should check the other way equality mechanism anyway.
            return MethodDesc == 0 && Type == other.Type && MetadataToken == other.MetadataToken;
        }

        public override int GetHashCode() => MethodDesc.GetHashCode();

        public static bool operator ==(ClrMethod? left, ClrMethod? right)
        {
            if (right is null)
                return left is null;

            return right.Equals(left);
        }

        public static bool operator !=(ClrMethod? left, ClrMethod? right) => !(left == right);

        public override string? ToString() => Signature;
    }
}
