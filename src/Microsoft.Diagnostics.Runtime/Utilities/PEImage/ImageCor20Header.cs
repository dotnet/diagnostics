// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ImageCor20Header
    {
        // Header versioning
        public uint cb;
        public ushort MajorRuntimeVersion;
        public ushort MinorRuntimeVersion;

        // Symbol table and startup information
        public ImageDataDirectory MetaData;
        public uint Flags;

        // The main program if it is an EXE (not used if a DLL?)
        // If COMIMAGE_FLAGS_NATIVE_ENTRYPOINT is not set, EntryPointToken represents a managed entrypoint.
        // If COMIMAGE_FLAGS_NATIVE_ENTRYPOINT is set, EntryPointRVA represents an RVA to a native entrypoint
        // (depricated for DLLs, use modules constructors intead).
        public ImageCor20HeaderEntrypoint EntryPoint;

        // This is the blob of managed resources. Fetched using code:AssemblyNative.GetResource and
        // code:PEFile.GetResource and accessible from managed code from
        // System.Assembly.GetManifestResourceStream.  The meta data has a table that maps names to offsets into
        // this blob, so logically the blob is a set of resources.
        public ImageDataDirectory Resources;
        // IL assemblies can be signed with a public-private key to validate who created it.  The signature goes
        // here if this feature is used.
        public ImageDataDirectory StrongNameSignature;

        public ImageDataDirectory CodeManagerTable; // Depricated, not used
                                                    // Used for manged codee that has unmaanaged code inside it (or exports methods as unmanaged entry points)
        public ImageDataDirectory VTableFixups;
        public ImageDataDirectory ExportAddressTableJumps;

        // null for ordinary IL images.  NGEN images it points at a code:CORCOMPILE_HEADER structure
        public ImageDataDirectory ManagedNativeHeader;
    }
}
