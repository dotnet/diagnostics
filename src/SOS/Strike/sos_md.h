// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// ==++==
// 
 
// 
// ==--==

#ifndef __SOS_MD_H__
#define __SOS_MD_H__

#define IfErrGoTo(s, label) { \
	hresult = (s); \
	if(FAILED(hresult)){ \
	  goto label; }}

class CQuickBytes;

// TODO: Cleanup code to allow SOS to directly include the metadata header files.
//#include "MetaData.h"
//#include "corpriv.h"

/*
 *
 * Metadata definitions needed for PrettyPrint functions.
 *   The original definitions for the types and interfaces below exist in
 *   inc\MetaData.h and inc\CorPriv.h
 * TODO:
 *   Cleanup code to allow SOS to directly include the metadata header files.
 *     Currently it's extremely difficult due to symbol redefinitions.
 *   Always keep the definitions below in sync with the originals.
 * NOTES:
 *   Since SOS runs in a native debugger session, since it does not use EnC,
 *      and in order to minimize the amount of duplication we changed the
 *      method definitions that deal with UTSemReadWrite* arguments to take
 *      void* arguments instead.
 *   Also, some of the interface methods take CQuickBytes as arguments.
 *      If these methods are ever used it becomes crucial to maintain binary
 *      compatibility b/w the CQuickBytes defined in SOS and the definition
 *      from the EE.
 *
 */

/********************************************************************************/

// Fine grained formatting flags used by the PrettyPrint APIs below.
// Upto FormatStubInfo they mirror the values used by TypeString, after that
// they're used to enable specifying differences between the ILDASM-style 
// output and the C#-like output prefered by the rest of SOS.
typedef enum 
{
  FormatBasic       =   0x00000000, // Not a bitmask, simply the tersest flag settings possible
  FormatNamespace   =   0x00000001, // Include namespace and/or enclosing class names in type names
  FormatFullInst    =   0x00000002, // Include namespace and assembly in generic types (regardless of other flag settings)
  FormatAssembly    =   0x00000004, // Include assembly display name in type names
  FormatSignature   =   0x00000008, // Include signature in method names
  FormatNoVersion   =   0x00000010, // Suppress version and culture information in all assembly names
  FormatDebug       =   0x00000020, // For debug printing of types only
  FormatAngleBrackets = 0x00000040, // Whether generic types are C<T> or C[T]
  FormatStubInfo    =   0x00000080, // Include stub info like {unbox-stub}
  // following flags are not present in TypeString::FormatFlags
  FormatSlashSep    =   0x00000100, // Whether nested types are NS.C1/C2 or NS.C1+C2
  FormatKwInNames   =   0x00000200, // Whether "class" and "valuetype" appear in type names in certain instances
  FormatCSharp      =   0x0000004b, // Used to generate a C#-like string representation of the token
  FormatILDasm      =   0x000003ff, // Used to generate an ILDASM-style string representation of the token
}
PPFormatFlags;

static char* asString(CQuickBytes *out);

PCCOR_SIGNATURE PrettyPrintType(
    PCCOR_SIGNATURE typePtr,            // type to convert,     
    CQuickBytes *out,                   // where to put the pretty printed string   
    IMetaDataImport *pIMDI,           // ptr to IMetaDataImport class with ComSig
    DWORD formatFlags = FormatILDasm);

const char* PrettyPrintClass(
    CQuickBytes *out,                   // where to put the pretty printed string   
	mdToken tk,					 		// The class token to look up 
    IMetaDataImport *pIMDI,           // ptr to IMetaDataImport class with ComSig
    DWORD formatFlags = FormatILDasm);

// We have a proliferation of functions that translate a (module/token) pair to
// a string, but none of them were as complete as PrettyPrintClass. Most were
// not handling generic instantiations appropriately. PrettyPrintClassFromToken
// provides this missing functionality. If passed "FormatCSharp" it will generate
// a name fitting the format used throughout SOS, with the exception of !dumpil
// (due to its ILDASM ancestry).
// TODO: Refactor the code in PrettyPrintClassFromToken, NameForTypeDef_s,
// TODO: NameForToken_s, MDInfo::TypeDef/RefName
void PrettyPrintClassFromToken(
    TADDR moduleAddr,                   // the module containing the token
    mdToken tok,                        // the class token to look up
    __out_ecount(cbName) WCHAR* mdName, // where to put the pretty printed string
    size_t cbName,                      // the capacity of the buffer
    DWORD formatFlags = FormatCSharp);  // the format flags for the types

#endif
