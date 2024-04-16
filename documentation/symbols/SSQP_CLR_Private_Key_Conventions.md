# SSQP CLR Private Key conventions #

These conventions are private extensions to the normal [SSQP conventions](SSQP_Key_Conventions.md). They fulfill niche scenarios specific to the CLR product and are not expected to be used within any general purpose index generating tool.

## Basic rules ##

The private conventions use the same basic rules for bytes, bytes sequences, integers, strings, etc as described in the standard conventions.

## Key formats ##


### PE-filesize-timestamp-coreclr

This key indexes an sos\*.dll or mscordaccore\*.dll file that should be used to debug a given coreclr.dll. The lookup key is computed similar to PE-timestamp-filesize except the timestamp and filesize values are taken from coreclr.dll rather than the file being indexed.
Example:

**File names:** `mscordaccore.dll, sos.dll or SOS.NETCore.dll`

**CoreCLR’s COFF header Timestamp field:** `0x542d5742`

**CoreCLR’s COFF header SizeOfImage field:** `0x32000`

**Lookup keys:** 

    mscordaccore.dll/542d574200032000/mscordaccore.dll
    sos.dll/542d574200032000/sos.dll
    SOS.NETCore.dll/542d574200032000/SOS.NETCore.dll


### ELF-buildid-coreclr

This applies to any file named libmscordaccore.so or libsos.so that should be used to debug a given libcoreclr.so. The key is computed similarly to ELF-buildid except the note bytes is retrieved from the libcoreclr.so file and prefixed with 'elf-buildid-coreclr-':

`<file_name>/elf-buildid-coreclr-<note_byte_sequence>/<file_name>`

Example:

**File names:** `libmscordaccore.so, libsos.so or SOS.NETCore.dll`

**libcoreclr.so’s build note bytes:** `0x18, 0x0a, 0x37, 0x3d, 0x6a, 0xfb, 0xab, 0xf0, 0xeb, 0x1f, 0x09, 0xbe, 0x1b, 0xc4, 0x5b, 0xd7, 0x96, 0xa7, 0x10, 0x85`

**Lookup keys:** 

    libmscordaccore.so/elf-buildid-coreclr-180a373d6afbabf0eb1f09be1bc45bd796a71085/libmscordaccore.so
    libsos.so/elf-buildid-coreclr-180a373d6afbabf0eb1f09be1bc45bd796a71085/libsos.so 
    sos-netcore.dll/elf-buildid-coreclr-180a373d6afbabf0eb1f09be1bc45bd796a71085/sos-netcore.dll


### Mach-uuid-coreclr

This applies to any file named libmscordaccore.dylib or libsos.dylib that should be used to debug a given libcoreclr.dylib. The key is computed similarly to Mach-uuid except the uuid is retrieved from the libcoreclr.dylib file and prefixed with 'mach-uuid-coreclr-':

`<file_name>/mach-uuid-coreclr-<uuid_bytes>/<file_name>`

Example:

**File names:** `libmscordaccore.dylb, libsos.dylib or SOS.NETCore.dll`

**libcoreclr.dylib’s uuid bytes:** `0x49, 0x7B, 0x72, 0xF6, 0x39, 0x0A, 0x44, 0xFC, 0x87, 0x8E, 0x5A, 0x2D, 0x63, 0xB6, 0xCC, 0x4B`

**Lookup keys:**

    libmscordaccore.dylib/mach-uuid-coreclr-497b72f6390a44fc878e5a2d63b6cc4b/libmscordaccore.dylib
    libsos.dylib/mach-uuid-coreclr-497b72f6390a44fc878e5a2d63b6cc4b/libsos.dylib
    sos.netcore.dll/mach-uuid-coreclr-497b72f6390a44fc878e5a2d63b6cc4b/sos.netcore.dll

