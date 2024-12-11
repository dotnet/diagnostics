# SSQP Key conventions #

When using [SSQP](Simple_Symbol_Query_Protocol.md) it is critical that the content publishers and content consumers agree what keys should correspond to which files. Although any publisher-consumer pair is free to create private agreements, using a standard key format offers the widest compatibility.


## Key formatting basic rules
Unless otherwise specified:

- Bytes: Convert to characters by splitting the byte into the most significant 4 bits and 4 least significant bits, each of which has value 0-15. Convert each of those chunks to the corresponding lower case hexadecimal character. Last concatenate the two characters putting the most significant bit chunk first. For example 0 => '00', 1 => '01', 45 => '2d', 185 => 'b9'
- Byte sequences: Convert to characters by converting each byte as above and then concatenating the characters. For example 2,45,4 => '022d04'
- Multi-byte integers: Convert to characters by first converting it to a big-endian byte sequence next convert the sequence as above and finally trim all leading '0' characters. Example 3,559,453,162 => 'd428f1ea', 114 => '72'
- strings: Convert all the characters to lower-case
- guid: The guid consists of a 4 byte integer, two 2 byte integers, and a sequence of 8 bytes. It is formatted by converting each portion to hex characters without trimming leading '0' characters on the integers, then concatenate the results. Example: { 0x097B72F6, 0x390A, 0x04FC, { 0x87, 0x8E, 0x5A, 0x2D, 0x63, 0xB6, 0xCC, 0x4B } } => '097b72f6390a04fc878e5a2d63b6cc4b'

## Key formats


### PE-timestamp-filesize
This key references Windows Portable Executable format files which commonly have .dll or .exe suffixes. The key is computed by extracting the Timestamp (4 byte integer) and SizeOfImage (4 byte integer) fields from the COFF header in PE image. The key is formatted:

`<filename>/<Timestamp><SizeOfImage>/<filename>`

Note that the timeStamp is always printed as eight digits (with leading zeroes as needed) using upper-case for ‘A’ to ‘F’ (important if your symbol server is case sensitive), whereas the image size is printed using as few digits as needed, in lower-case.

Example:
	
**File name:** `Foo.exe`

**COFF header Timestamp field:** `0x542d574e`

**COFF header SizeOfImage field:** `0xc2000`

**Lookup key:** `foo.exe/542D574Ec2000/foo.exe`


### PDB-Signature-Age

This applies to Microsoft C++ Symbol Format, commonly called PDB and using files with the .pdb file extension. The key is computed by extracting the Signature (guid) and Age (4 byte integer) values from the guid stream within MSF container. The final key is formatted:

`<filename>/<Signature><Age>/<filename>`

Example:

**File name:** `Foo.pdb`

**Signature field:** `{ 0x497B72F6, 0x390A, 0x44FC, { 0x87, 0x8E, 0x5A, 0x2D, 0x63, 0xB6, 0xCC, 0x4B } }`

**Age field:** `0x1`

**Lookup key**: `foo.pdb/497b72f6390a44fc878e5a2d63b6cc4b1/foo.pdb`

### PDZ-Signature-Age

This applies to the Microsoft C++ Symbol Format with compressed streams, known as PDZ or msfz, also commonly saved with the pdb extension. 

Like regular C++ PDBs, the key also uses values extracted from the GUID stream which is uncompressed. Additionally, the index contains a marker for the type ('msfz') and version (currently only '0'):

`<filename>/<Signature><Age>/msfz<version>/<filename>`

Example:

**File name:** `Foo.pdb`

**Signature field:** `{ 0x497B72F6, 0x390A, 0x44FC, { 0x87, 0x8E, 0x5A, 0x2D, 0x63, 0xB6, 0xCC, 0x4B } }`

**Age field:** `0x1`

**Format version:** `0`

**Lookup key**: `foo.pdb/497b72f6390a44fc878e5a2d63b6cc4b1/msfz0/foo.pdb`

### Portable-Pdb-Signature

This applies to Microsoft .Net portable PDB format files, commonly using the suffix .pdb. The Portable PDB format uses the same key format as Windows PDBs, except that 0xFFFFFFFF (UInt32.MaxValue) is used for the age. In other words, the key is computed by extracting the Signature (guid) from debug metadata header and combining it with 'FFFFFFFF'. The final key is formatted: 

`<filename>/<guid>FFFFFFFF/<filename>`
 
Example:
	
**File name:** `Foo.pdb`

**Signature field:** `{ 0x497B72F6, 0x390A, 0x44FC { 0x87, 0x8E, 0x5A, 0x2D, 0x63, 0xB6, 0xCC, 0x4B } }`

**Lookup key:** `foo.pdb/497b72f6390a44fc878e5a2d63b6cc4bFFFFFFFF/foo.pdb`


### ELF-buildid

The ELF format files indexed with the ELF-buildid suffix are expected to be the exact image that is loaded in a process or core dump, doesn’t require the module to be stripped of its symbols (although it usually is), and commonly uses the .so suffix or no suffix. The key is computed by reading the 20 byte sequence of the ELF Note section that is named “GNU” and that has note type GNU Build Id (3). If byte sequence is smaller than 20 bytes, bytes of value 0x00 should be added until the byte sequence is 20 bytes long. The final key is formatted:

`<file_name>/elf-buildid-<note_byte_sequence>/<file_name>`

Example:

**File name:** `foo.so`

**Build note bytes:** `0x18, 0x0a, 0x37, 0x3d, 0x6a, 0xfb, 0xab, 0xf0, 0xeb, 0x1f, 0x09, 0xbe, 0x1b, 0xc4, 0x5b, 0xd7, 0x96, 0xa7, 0x10, 0x85`

**Lookup key:** `foo.so/elf-buildid-180a373d6afbabf0eb1f09be1bc45bd796a71085/foo.so`


### ELF-buildid-sym

The ELF format files indexed with the ELF-buildid-sym suffix are the result of the stripping process and contain only the symbols from the ELF-buildid indexed module. They commonly end in ‘.debug’, ‘.so.dbg’ or ‘.dbg’. The key is computed by reading the 20 byte sequence of the ELF Note section that is named “GNU” and that has note type GNU Build Id (3). If byte sequence is smaller than 20 bytes, bytes of value 0x00 should be added until the byte sequence is 20 bytes long. The file name is not used in the index because there are cases where all we have is the build id. The final key is formatted:

`_.debug/elf-buildid-sym-<note_byte_sequence>/_.debug`

Example:

**File name:** `foo.so.dbg`

**Build note bytes:** `0x18, 0x0a, 0x37, 0x3d, 0x6a, 0xfb, 0xab, 0xf0, 0xeb, 0x1f, 0x09, 0xbe, 0x1b, 0xc4, 0x5b, 0xd7, 0x96, 0xa7, 0x10, 0x85`

**Lookup key:** `_.debug/elf-buildid-sym-180a373d6afbabf0eb1f09be1bc45bd796a71085/_.debug`

Example:

**File name:** `bar.so.dbg`

**Build note bytes:** `0x18, 0x0a, 0x37, 0x3d, 0x6a, 0xfb, 0xab, 0xf0, 0xeb, 0x1f, 0x09, 0xbe, 0x1b, 0xc4, 0x5b, 0xd7`

**Lookup key:** `_.debug/elf-buildid-sym-180a373d6afbabf0eb1f09be1bc45bd700000000/_.debug`


### Mach-uuid
This applies to any MachO format files that have been stripped of debugging information, commonly ending in 'dylib'. The key is computed by reading the uuid byte sequence of the MachO LC_UUID load command. The final key is formatted:

`<file_name>/mach-uuid-<uuid_bytes>/<file_name>`

Example:

**File name:** `foo.dylib`

**Uuid bytes:** `0x49, 0x7B, 0x72, 0xF6, 0x39, 0x0A, 0x44, 0xFC, 0x87, 0x8E, 0x5A, 0x2D, 0x63, 0xB6, 0xCC, 0x4B`

**Lookup key:** `foo.dylib/mach-uuid-497b72f6390a44fc878e5a2d63b6cc4b/foo.dylib`


### Mach-uuid-sym

This applies to any MachO format files that have not been stripped of debugging information, commonly ending in '.dylib.dwarf'. The key is computed by reading the uuid byte sequence of the MachO LC_UUID load command. The final key is formatted:

`_.dwarf/mach-uuid-sym-<uuid_bytes>/_.dwarf`

Example:

**File name:** `foo.dylib.dwarf`

**Uuid bytes:** `0x49, 0x7B, 0x72, 0xF6, 0x39, 0x0A, 0x44, 0xFC, 0x87, 0x8E, 0x5A, 0x2D, 0x63, 0xB6, 0xCC, 0x4B`

**Lookup key:** `_.dwarf/mach-uuid-sym-497b72f6390a44fc878e5a2d63b6cc4b/_.dwarf`


### SHA1

This applies to any file, but is commonly used on sources. The key is computed by calculating a SHA1 hash, then formatting the 20 byte hash sequence prepended with “sha1-“

Example:

**File name:** `Foo.cs`

**Sha1 hash bytes:** `0x49, 0x7B, 0x72, 0xF6, 0x39, 0x0A, 0x44, 0xFC, 0x87, 0x8E, 0x5A, 0x2D, 0x63, 0xB6, 0xCC, 0x4B, 0x0C, 0x2D, 0x99, 0x84`

**Lookup key:** `foo.cs/sha1-497b72f6390a44fc878e5a2d63b6cc4b0c2d9984/foo.cs`

### R2R PerfMap v1

This applies to v1 PerfMap files produced by CrossGen2 , commonly having extensions `.ni.r2rmap`. The key is formed by formatting the signature, the file name, and the version in the following manner:

Example:

**File name:** `System.Private.CoreLib.ni.r2rmap`

**Signature at pseudo-rva 0xFFFFFFFF:** `f5fddf60efb0bee79ef02a19c3decba9`

**Version at pseudo-rva 0xFFFFFFFE:** `1`

**Lookup key:** `system.private.corelib.ni.r2rmap/r2rmap-v1-f5fddf60efb0bee79ef02a19c3decba9/system.private.corelib.ni.r2rmap`


### JavaScript Source Maps

JavaScript source maps, which are used by browser developer tools to provide source-level debugging experiences, are [standardized](https://sourcemaps.info). These can be indexed by the 
[SHA-256 hash of the script file they map](https://chromedevtools.github.io/devtools-protocol/tot/Debugger/#event-scriptParsed)
(see the `hash` property of the linked event).

Example:

**File name:** `main.js`

**SHA-256 of file:** `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`

**Lookup key:**: `main.js.map/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/main.js.map`


### WASM (WebAssembly) Modules

WebAssembly symbols, which can be used by browser developer tools to provide source-level debugging experiences, are based on the DWARF format. These are indexed by their DWARF Build ID 
(built with `-Wl,--build-id` arguments) and the name of the module being debugged via the 
[buildId property](https://chromedevtools.github.io/devtools-protocol/tot/Debugger/#event-scriptParsed), and the symbol file itself is suffixed with `.s` to disambiguate from the WASM file.

**File name:** `main.wasm`

**Build ID of file:** `e3b0c44298fc1c149afbf4c8996fb92427ae41e4`

**Lookup key:**: `main.wasm.s/e3b0c44298fc1c149afbf4c8996fb92427ae41e4/main.wasm.s`
