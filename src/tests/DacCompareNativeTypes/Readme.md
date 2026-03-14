## DacCompareNativeTypes

The `DacCompareNativeTypes` app is currently more of a testing tool than
a test app.

It is designed to compare the structure/class type layouts native C/C++ 
types.

It is currently designed to compare `dwarf` to `pdb` but could be easily
extended to support other combinations.

## Running type comparison comparison

The program consumes the out put of two other programs:

+ The `dwarfdump` Linux app.  
+ The `Dia2Dump` sample app included with Visual Studio.  

The app is a bit crude currently.  It looks for hardcoded `pdb` and `dwarf` files instead of taking command line arguments.

### Expected `dwarf` format

DwarfParse.cs is currently written to expect the output from `dwarfdump -i -d -G`.

Because the output is verbose, it is worth stripping away unneeded tags.

This is the command that has been used:

`dwarfdump -i -d -G <path>/libmscordaccore.so | grep -v -e DW_TAG_formal -e DW_TAG_subprog > dwarf`

### Expected `pdb` format

The `PdbParser.cs` is currently written to expect the output from:

`Dia2Dump.exe -t <path>\mscordaccore.dll > pdb`

### Running DacCompareNativeTypes

`dotnet run`

## What it does?

It parses the `pdb` and `dwarf` files and creates a C# layout description 
for each type found.  The layout description is focused on comparing 
member names and offsets.

To make comparison simpler
+ Type definitions w/o members are discarded. These are presumend forward definitions
+ White space is stripped from template arguments
+ The type names of members and base classes are ignored.  This is primarily because
the size of basic types are not identical.  So it is problemenatic to compare a type
like `long` as a type or and a template argument.
+ Only types which exist in both `dwarf` and `pdb` are compared.

These compromises obviously allows some possible type mismatches to exist.
The focus on the tool is on catching the bulk of accidental type layout
mismatches.

## Output

The program compares types with the same name and reports thise with differences.
It dumps both the dwarf layout and the pdb layout.

### Differences are expected

Currently the comparison includes baseline differences. These have been
reviewed and appear innocuous.

Therefore, any automation would need to consider and flag differences from baseline.
