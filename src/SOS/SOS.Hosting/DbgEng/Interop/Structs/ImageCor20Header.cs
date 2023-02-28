// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct IMAGE_COR20_HEADER
    {
        // Header versioning
        public uint cb;
        public ushort MajorRuntimeVersion;
        public ushort MinorRuntimeVersion;

        // Symbol table and startup information
        public IMAGE_DATA_DIRECTORY MetaData;
        public uint Flags;

        // The main program if it is an EXE (not used if a DLL?)
        // If COMIMAGE_FLAGS_NATIVE_ENTRYPOINT is not set, EntryPointToken represents a managed entrypoint.
        // If COMIMAGE_FLAGS_NATIVE_ENTRYPOINT is set, EntryPointRVA represents an RVA to a native entrypoint
        // (deprecated for DLLs, use modules constructors instead).
        public IMAGE_COR20_HEADER_ENTRYPOINT EntryPoint;

        // This is the blob of managed resources. Fetched using code:AssemblyNative.GetResource and
        // code:PEFile.GetResource and accessible from managed code from
        // System.Assembly.GetManifestResourceStream.  The meta data has a table that maps names to offsets into
        // this blob, so logically the blob is a set of resources.
        public IMAGE_DATA_DIRECTORY Resources;
        // IL assemblies can be signed with a public-private key to validate who created it.  The signature goes
        // here if this feature is used.
        public IMAGE_DATA_DIRECTORY StrongNameSignature;

        public IMAGE_DATA_DIRECTORY CodeManagerTable; // Deprecated, not used
        // Used for managed code that has unmanaged code inside it (or exports methods as unmanaged entry points)
        public IMAGE_DATA_DIRECTORY VTableFixups;
        public IMAGE_DATA_DIRECTORY ExportAddressTableJumps;

        // null for ordinary IL images.  NGEN images it points at a code:CORCOMPILE_HEADER structure
        public IMAGE_DATA_DIRECTORY ManagedNativeHeader;
    }
}
