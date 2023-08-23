// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Interfaces;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents a managed module in the target process.
    /// </summary>
    public sealed class ClrModule : IClrModule
    {
        private readonly IClrModuleHelpers _helpers;
        private int _debugMode = int.MaxValue;
        private MetadataImport? _metadata;
        private PdbInfo? _pdb;
        private readonly bool _isReflection;
        private (ulong MethodTable, int Token)[]? _typeDefMap;
        private (ulong MethodTable, int Token)[]? _typeRefMap;
        private ClrExtendedModuleData? _extendedData;
        private ulong? _size;

        private ClrExtendedModuleData ExtendedData => _extendedData ??= _helpers.GetExtendedData(this);

        internal ClrModule(ClrAppDomain domain, ulong address, IClrModuleHelpers helpers, in ModuleData data)
        {
            _helpers = helpers;
            AppDomain = domain;
            AssemblyAddress = data.Assembly;
            Address = address;
            IsPEFile = data.IsPEFile != 0;
            ImageBase = data.ILBase;
            MetadataAddress = data.MetadataStart;
            MetadataLength = data.MetadataSize;
            _isReflection = data.IsReflection != 0;
            ThunkHeap = data.ThunkHeap;
            LoaderAllocator = data.LoaderAllocator;
        }

        internal ClrModule(ClrAppDomain domain, IClrModuleHelpers helpers, ulong address)
        {
            _helpers = helpers;
            AppDomain = domain;
            Address = address;
        }

        /// <summary>
        /// Gets the address of the clr!Module object.
        /// </summary>
        public ulong Address { get; }

        /// <summary>
        /// Gets the AppDomain parent of this module.
        /// </summary>
        public ClrAppDomain AppDomain { get; }

        IClrAppDomain IClrModule.AppDomain => AppDomain;

        /// <summary>
        /// Gets the name of the assembly that this module is defined in.
        /// </summary>
        public string? AssemblyName => _helpers.GetAssemblyName(this) ?? ExtendedData.FileName;

        /// <summary>
        /// Gets an identifier to uniquely represent this assembly.  This value is not used by any other
        /// function in ClrMD, but can be used to group modules by their assembly.  (Do not use AssemblyName
        /// for this, as reflection and other special assemblies can share the same name, but actually be
        /// different.)
        /// </summary>
        public ulong AssemblyAddress { get; }

        /// <summary>
        /// Gets the name of the module.
        /// </summary>
        public string? Name => ExtendedData.FileName ?? AssemblyName;

        /// <summary>
        /// Gets a value indicating whether this module was created through <c>System.Reflection.Emit</c> (and thus has no associated
        /// file).
        /// </summary>
        public bool IsDynamic => _isReflection || ExtendedData.IsDynamic;

        /// <summary>
        /// Gets a value indicating whether this module is an actual PEFile on disk.
        /// </summary>
        public bool IsPEFile { get; }

        /// <summary>
        /// Gets the base of the image loaded into memory.  This may be 0 if there is not a physical
        /// file backing it.
        /// </summary>
        public ulong ImageBase { get; }

        /// <summary>
        /// Returns the in memory layout for PEImages.
        /// </summary>
        public ModuleLayout Layout => ExtendedData.IsFlatLayout ? ModuleLayout.Flat : ModuleLayout.Unknown;

        /// <summary>
        /// Gets the size of the image in memory.
        /// </summary>
        public ulong Size => _size ??= GetSize();

        /// <summary>
        /// Gets the location of metadata for this module in the process's memory.  This is useful if you
        /// need to manually create IMetaData* objects.
        /// </summary>
        public ulong MetadataAddress { get; }

        /// <summary>
        /// Gets the length of the metadata for this module.
        /// </summary>
        public ulong MetadataLength { get; }

        /// <summary>
        /// Gets the <c>IMetaDataImport</c> interface for this module.  Note that this API does not provide a
        /// wrapper for <c>IMetaDataImport</c>.  You will need to wrap the API yourself if you need to use this.
        /// </summary>
        internal MetadataImport? MetadataImport => _metadata ??= _helpers.GetMetadataImport(this);

        /// <summary>
        /// The ThunkHeap associated with this Module.  This is only available when debugging a .Net 8 or
        /// later runtime.
        /// </summary>
        public ulong ThunkHeap { get; }

        /// <summary>
        /// The LoaderAllocator associated with this Module.  This is only available when debugging a .Net 8 or
        /// later runtime.  Note that this LoaderAllocator is usually share with its parent domain, except in
        /// rare circumstances, like for collectable assemblies.
        /// </summary>
        public ulong LoaderAllocator { get; }

        /// <summary>
        /// Enumerates the native heaps associated with the ThunkHeap.
        /// </summary>
        /// <returns>An enumerable of heaps.</returns>
        public IEnumerable<ClrNativeHeapInfo> EnumerateThunkHeap() => _helpers.GetNativeHeapHelpers().EnumerateThunkHeaps(ThunkHeap);

        /// <summary>
        /// Enumerates the native heaps associated with the LoaderAllocator.  This may be the same set of
        /// heaps enumerated by ClrAppDomain.EnumerateLoaderAllocatorHeaps if LoaderAllocator is not 0 and
        /// equals ClrAppDomain.LoaderAllocator.
        /// </summary>
        /// <returns>An enumerable of heaps.</returns>
        public IEnumerable<ClrNativeHeapInfo> EnumerateLoaderAllocatorHeaps() => _helpers.GetNativeHeapHelpers().EnumerateLoaderAllocatorNativeHeaps(LoaderAllocator);

        /// <summary>
        /// Gets the debugging attributes for this module.
        /// </summary>
        public DebuggableAttribute.DebuggingModes DebuggingMode
        {
            get
            {
                if (_debugMode == int.MaxValue)
                    _debugMode = GetDebugAttribute();

                DebugOnly.Assert(_debugMode != int.MaxValue);
                return (DebuggableAttribute.DebuggingModes)_debugMode;
            }
        }

        private unsafe int GetDebugAttribute()
        {
            MetadataImport? metadata = MetadataImport;
            if (metadata != null)
            {
                try
                {
                    if (metadata.GetCustomAttributeByName(0x20000001, "System.Diagnostics.DebuggableAttribute", out IntPtr data, out uint cbData) && cbData >= 4)
                    {
                        byte* b = (byte*)data.ToPointer();
                        ushort opt = b[2];
                        ushort dbg = b[3];

                        return (dbg << 8) | opt;
                    }
                }
                catch (SEHException)
                {
                }
            }

            return (int)DebuggableAttribute.DebuggingModes.None;
        }

        /// <summary>
        /// Enumerates the constructed methodtables in this module which correspond to typedef tokens defined by this module.
        /// </summary>
        /// <returns>An enumeration of (ulong methodTable, uint typeDef).</returns>
        public IEnumerable<(ulong MethodTable, int Token)> EnumerateTypeDefToMethodTableMap() => _typeDefMap ??= _helpers.EnumerateTypeDefMap(this).ToArray();

        public IEnumerable<(ulong MethodTable, int Token)> EnumerateTypeRefToMethodTableMap() => _typeRefMap ??= _helpers.EnumerateTypeRefMap(this).ToArray();

        /// <summary>
        /// Attempts to obtain a ClrType based on the name of the type.  Note this is a "best effort" due to
        /// the way that the DAC handles types.  This function will fail for Generics, and types which have
        /// never been constructed in the target process.  Please be sure to null-check the return value of
        /// this function.
        /// </summary>
        /// <param name="name">The name of the type.  (This would be the EXACT value returned by ClrType.Name.)</param>
        /// <returns>The requested ClrType, or <see langword="null"/> if the type doesn't exist or if the runtime hasn't constructed it.</returns>
        public ClrType? GetTypeByName(string name) => AppDomain.Runtime.Heap.GetTypeByName(this, name);

        IClrType? IClrModule.GetTypeByName(string name) => GetTypeByName(name);

        /// <summary>
        /// Returns a name for the assembly.
        /// </summary>
        /// <returns>A name for the assembly.</returns>
        public override string? ToString()
        {
            if (string.IsNullOrEmpty(Name))
            {
                if (!string.IsNullOrEmpty(AssemblyName))
                    return AssemblyName;

                if (IsDynamic)
                    return "dynamic";
            }

            return Name;
        }

        /// <summary>
        /// Gets the PDB information for this module.
        /// </summary>
        public PdbInfo? Pdb
        {
            get
            {
                if (_pdb is null)
                {
                    using PEImage pefile = GetPEImage();
                    if (pefile.IsValid)
                        _pdb = pefile.DefaultPdb;
                }

                return _pdb;
            }

        }

        private ulong GetSize()
        {
            ulong size = ExtendedData.Size;
            if (size != 0)
                return size;

            try
            {
                using PEImage peimage = GetPEImage();
                if (peimage.IsValid)
                {
                    unchecked
                    {
                        size = (ulong)peimage.IndexFileSize;
                    }
                }
            }
            catch
            {
            }

            return size;
        }

        private PEImage GetPEImage()
        {
            // Not correct, but as close as we can get until we add more information to the dac.
            bool virt = Layout != ModuleLayout.Flat;
            long size = (long)ExtendedData.Size;

            ReadVirtualStream stream = new(_helpers.DataReader, (long)ImageBase, size > 0 ? size : int.MaxValue);
            return new(stream, leaveOpen: false, isVirtual: virt);
        }

        public override bool Equals(object? obj) => Equals(obj as ClrModule);

        public bool Equals(ClrModule? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return Address == other.Address;
        }

        public bool Equals(IClrModule? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return Address == other.Address;
        }

        public override int GetHashCode() => Address.GetHashCode();

        public static bool operator ==(ClrModule? left, ClrModule? right)
        {
            if (right is null)
                return left is null;

            return right.Equals(left);
        }

        public static bool operator !=(ClrModule? left, ClrModule? right) => !(left == right);
    }
}