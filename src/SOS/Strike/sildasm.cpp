// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

// ==++==
// 
 
// 
// ==--==
//
// disasm.cpp : Defines the entry point for the console application.
//
#ifndef FEATURE_PAL
#include <tchar.h>
#endif
#include "strike.h"
#include "util.h"
#include "strsafe.h"
//#ifndef FEATURE_PAL
//#include "gcinfo.h"
//#endif
#include "disasm.h"
#include <dbghelp.h>

#include "corhdr.h"

#include "cor.h"
#include "dacprivate.h"

#include "openum.h"

#include "sos_md.h"

#define _BLD_CLR 1
#define SOS_INCLUDE 1
#include "corhlpr.h"
#include "corhlpr.cpp"

#include "sildasm.h"

#include <functional>

//////////////////////////////////////////////////////////////////////////////////////////////////////////
#undef printf
#define printf ExtOut

// typedef unsigned char BYTE;
struct OpCode
{
    int code;
    const char *name;
    int args;
    BYTE b1;
    BYTE b2;

    unsigned int getCode() { 
        if (b1==0xFF) return b2;
        else return (0xFE00 | b2);
    }
};

#define OPCODES_LENGTH 0x122

#undef OPDEF
#define OPDEF(c,s,pop,push,args,type,l,s1,s2,ctrl) {c, s, args, s1, s2},
static OpCode opcodes[] =
{
#include "opcode.def"
};

// The UNALIGNED is because on IA64 alignment rules would prevent
// us from reading a pointer from an unaligned source.
template <typename T>
T ReadData (const BYTE* const pBuffer, ULONG& position) {
    T val = *((T UNALIGNED*)(pBuffer+position));
    position += sizeof(T);
    return val;
}

unsigned int ReadOpcode(const BYTE* const pBuffer, ULONG& position)
{
    unsigned int c = ReadData<BYTE>(pBuffer, /* byref */position);
    if (c == 0xFE)
    {
        c = ReadData<BYTE>(pBuffer, /* byref */ position);
        c |= 0x100;
    }
    return c;
}

void DisassembleToken(IMetaDataImport *i,
                      DWORD token)
{
    class MethodSigArgPrettyPrinter
    {
        SigParser sigParser;
        ULONG cParamTypes = 0;
        bool isField = true;
        IMetaDataImport *pMDImport;

    public: 
        MethodSigArgPrettyPrinter(PCCOR_SIGNATURE sig, ULONG cbSig, IMetaDataImport *_pMDImport) : sigParser(sig, cbSig), pMDImport(_pMDImport)
        {
        }

        void HandleReturnType()
        {
            HRESULT hr;
            ULONG callConv;
            hr = sigParser.GetCallingConvInfo(&callConv);
            if (SUCCEEDED(hr))
            {
                isField = ((callConv & IMAGE_CEE_CS_CALLCONV_FIELD) == IMAGE_CEE_CS_CALLCONV_FIELD);

                if (!isField)
                {
                    // Discard generic arg count
                    if ((callConv & IMAGE_CEE_CS_CALLCONV_GENERIC) == IMAGE_CEE_CS_CALLCONV_GENERIC)
                    {
                        ULONG unused;
                        hr = sigParser.GetData(&unused);
                    }
                }

                if (SUCCEEDED(hr))
                {
                    // Get arg count;
                    hr = sigParser.GetData(&cParamTypes);
                    if (SUCCEEDED(hr))
                    {
                        // Print type
                        CQuickBytes out;
                        PrettyPrintType(sigParser.GetPtr(), &out, pMDImport);
                        int cchString = MultiByteToWideChar (CP_ACP, 0, asString(&out), -1, NULL, 0);
                        WCHAR *psz = new WCHAR[cchString];
                        MultiByteToWideChar (CP_ACP, 0, asString(&out), -1, psz, cchString);
                        printf("%S ", psz);
                        delete[] psz;
                        sigParser.SkipExactlyOne();
                    }
                }
            }
        }

        void HandleArguments()
        {
            if (!isField)
            {
                printf("(");
                for (ULONG paramIndex = 0; paramIndex < cParamTypes; paramIndex++)
                {
                    CQuickBytes out;
                    PrettyPrintType(sigParser.GetPtr(), &out, pMDImport);
                    int cchString = MultiByteToWideChar (CP_ACP, 0, asString(&out), -1, NULL, 0);
                    WCHAR *psz = new WCHAR[cchString];
                    MultiByteToWideChar (CP_ACP, 0, asString(&out), -1, psz, cchString);
                    if ((paramIndex + 1) < cParamTypes)
                        printf("%S,", psz);
                    else
                        printf("%S", psz);
                    delete[] psz;
                    sigParser.SkipExactlyOne();
                }
                printf(")");
            }
        }
    };

    HRESULT hr;

    switch (TypeFromToken(token))
    {
    default:
        printf("<unknown token type %08x>", TypeFromToken(token));
        break;

    case mdtTypeDef:
        {
            ULONG cLen;
            WCHAR szName[50];

            hr = i->GetTypeDefProps(token, szName, 49, &cLen, NULL, NULL);

            if (FAILED(hr))
                StringCchCopyW(szName, COUNTOF(szName), W("<unknown type def>"));

            printf("%S", szName);
        }
        break;

    case mdtTypeRef:
        {
            ULONG cLen;
            WCHAR szName[50];

            hr = i->GetTypeRefProps(token, NULL, szName, 49, &cLen);

            if (FAILED(hr))
                StringCchCopyW(szName, COUNTOF(szName), W("<unknown type ref>"));

            printf("%S", szName);
        }
        break;

    case mdtFieldDef:
        {
            ULONG cLen;
            WCHAR szFieldName[50];
            WCHAR szClassName[50];
            mdTypeDef mdClass;

            hr = i->GetFieldProps(token, &mdClass, szFieldName, 49, &cLen,
                                  NULL, NULL, NULL, NULL, NULL, NULL);

            if (FAILED(hr))
                StringCchCopyW(szFieldName, COUNTOF(szFieldName), W("<unknown field def>"));

            hr = i->GetTypeDefProps(mdClass, szClassName, 49, &cLen,
                                    NULL, NULL);

            if (FAILED(hr))
                StringCchCopyW(szClassName, COUNTOF(szClassName), W("<unknown type def>"));

            printf("%S::%S", szClassName, szFieldName);
        }
        break;

    case mdtMethodDef:
        {
            ULONG cLen;
            WCHAR szFieldName[50];
            WCHAR szClassName[50];
            mdTypeDef mdClass;
            PCCOR_SIGNATURE sig;
            ULONG cbSigBlob;

            hr = i->GetMethodProps(token, &mdClass, szFieldName, 49, &cLen,
                                   NULL, &sig, &cbSigBlob, NULL, NULL);

            MethodSigArgPrettyPrinter methodPrettyPrinter(sig, cbSigBlob, i);

            if (FAILED(hr))
                StringCchCopyW(szFieldName, COUNTOF(szFieldName), W("<unknown method def>"));
            else
                methodPrettyPrinter.HandleReturnType();

            hr = i->GetTypeDefProps(mdClass, szClassName, 49, &cLen,
                                    NULL, NULL);

            if (FAILED(hr))
                StringCchCopyW(szClassName, COUNTOF(szClassName), W("<unknown type def>"));

            printf("%S::%S", szClassName, szFieldName);
            methodPrettyPrinter.HandleArguments(); // Safe to call in all cases if HandleReturnType hasn't been called. Will do nothing.
        }
        break;

    case mdtMemberRef:
        {
            mdTypeRef cr = mdTypeRefNil;
            LPCWSTR pMemberName;
            WCHAR memberName[50];
            ULONG memberNameLen;
            PCCOR_SIGNATURE sig;
            ULONG cbSigBlob;

            hr = i->GetMemberRefProps(token, &cr, memberName, 49,
                                      &memberNameLen, &sig, &cbSigBlob);

            MethodSigArgPrettyPrinter methodPrettyPrinter(sig, cbSigBlob, i);

            if (FAILED(hr))
            {
                pMemberName = W("<unknown member ref>");
            }
            else
            {
                pMemberName = memberName;
                methodPrettyPrinter.HandleReturnType();
            }

            ULONG cLen;
            WCHAR szName[50];

            if (TypeFromToken(cr) == mdtTypeRef)
            {
                if (FAILED(i->GetTypeRefProps(cr, NULL, szName, 50, &cLen)))
                {
                    StringCchCopyW(szName, COUNTOF(szName), W("<unknown type ref>"));
                }
            }
            else if (TypeFromToken(cr) == mdtTypeDef)
            {
                if (FAILED(i->GetTypeDefProps(cr, szName, 49, &cLen,
                    NULL, NULL)))
                {
                    StringCchCopyW(szName, COUNTOF(szName), W("<unknown type def>"));
                }
            }
            else if (TypeFromToken(cr) == mdtTypeSpec)
            {
                CQuickBytes out;
                ULONG cSig;
                PCCOR_SIGNATURE sig;
                if (FAILED(i->GetTypeSpecFromToken(cr, &sig, &cSig)))
                {
                    StringCchCopyW(szName, COUNTOF(szName), W("<Invalid record>"));
                }
                else
                {
                    PrettyPrintType(sig, &out, i);
                    MultiByteToWideChar(CP_ACP, 0, asString(&out), -1, szName, 50);
                }
            }
            else
            {
                StringCchCopyW(szName, COUNTOF(szName), W("<unknown type token>"));
            }

            printf("%S::%S", szName, pMemberName);
            methodPrettyPrinter.HandleArguments(); // Safe to call in all cases if HandleReturnType hasn't been called. Will do nothing.
        }
        break;

    case mdtString:
        {
            ULONG numChars;
            WCHAR str[84];

            if (i->GetUserString((mdString)token, str, 80, &numChars) == S_OK)
            {
                if (numChars < 80)
                    str[numChars] = 0;
                wcscpy_s(&str[79], 4, W("..."));
                WCHAR* ptr = str;
                while (*ptr != 0) {
                    if (*ptr < 0x20 || *ptr >= 0x80) {
                        *ptr = '.';
                    }
                    ptr++;
                }

                printf("\"%S\"", str);
            }
            else
            {
                printf("STRING %x", token);
            }
        }
        break;
    }
}

ULONG GetILSize(DWORD_PTR ilAddr)
{
    ULONG uRet = 0;

    // workaround: read enough bytes at ilAddr to presumably get the entire header.
    // Could be error prone.

    alignas(COR_ILMETHOD) static BYTE headerArray[1024];
    HRESULT Status = g_ExtData->ReadVirtual(TO_CDADDR(ilAddr), headerArray, sizeof(headerArray), NULL);    
    if (SUCCEEDED(Status))
    {            
        COR_ILMETHOD_DECODER header((COR_ILMETHOD *)headerArray);
        // uRet = header.GetHeaderSize();
        uRet = header.GetOnDiskSize((COR_ILMETHOD *)headerArray);
    }

    return uRet;
}
  
HRESULT DecodeILFromAddress(IMetaDataImport *pImport, TADDR ilAddr)
{
    HRESULT Status = S_OK;

    ULONG Size = GetILSize(ilAddr);
    if (Size == 0)
    {
        ExtOut("error decoding IL\n");
        return Status;
    }

    ExtOut("ilAddr = %p\n", SOS_PTR(ilAddr));

    // Read the memory into a local buffer
    ArrayHolder<BYTE> pArray = new BYTE[Size];
    Status = g_ExtData->ReadVirtual(TO_CDADDR(ilAddr), pArray, Size, NULL);
    if (Status != S_OK)
    {
        ExtOut("Failed to read memory\n");
        return Status;
    }

    DecodeIL(pImport, pArray, Size);

    return Status;
}

void DecodeIL(IMetaDataImport *pImport, BYTE *buffer, ULONG bufSize)
{
    // First decode the header
    COR_ILMETHOD *pHeader = (COR_ILMETHOD *) buffer;    
    COR_ILMETHOD_DECODER header(pHeader);    

    ULONG position = 0;
    BYTE* pBuffer = const_cast<BYTE*>(header.Code);

    UINT indentCount = 0;
    ULONG endCodePosition = header.GetCodeSize();

    while (position < endCodePosition)
    {
        std::tuple<ULONG, UINT> r = DecodeILAtPosition(
                pImport, pBuffer, bufSize,
                position, indentCount, header);
        position = std::get<0>(r);
        indentCount = std::get<1>(r);
        printf("\n");
    }
}

std::tuple<ULONG, UINT> DecodeILAtPosition(
        IMetaDataImport *pImport, BYTE *pBuffer, ULONG bufSize,
        ULONG position, UINT indentCount, COR_ILMETHOD_DECODER& header)
{
    for (unsigned e=0;e<header.EHCount();e++)
    {
        IMAGE_COR_ILMETHOD_SECT_EH_CLAUSE_FAT ehBuff;
        const IMAGE_COR_ILMETHOD_SECT_EH_CLAUSE_FAT* ehInfo;

        ehInfo = header.EH->EHClause(e,&ehBuff);
        if (ehInfo->TryOffset == position)
        {
            printf ("%*s.try\n%*s{\n", indentCount, "", indentCount, "");
            indentCount+=2;
        }
        else if ((ehInfo->TryOffset + ehInfo->TryLength) == position)
        {
            indentCount-=2;
            printf("%*s} // end .try\n", indentCount, "");
        }

        if (ehInfo->HandlerOffset == position)
        {
            if (ehInfo->Flags == COR_ILEXCEPTION_CLAUSE_FINALLY)
                printf("%*s.finally\n%*s{\n", indentCount, "", indentCount, "");
            else
                printf("%*s.catch\n%*s{\n", indentCount, "", indentCount, "");

            indentCount+=2;
        }
        else if ((ehInfo->HandlerOffset + ehInfo->HandlerLength) == position)
        {
            indentCount-=2;

            if (ehInfo->Flags == COR_ILEXCEPTION_CLAUSE_FINALLY)
                printf("%*s} // end .finally\n", indentCount, "");
            else
                printf("%*s} // end .catch\n", indentCount, "");
        }
    }
    std::function<void(DWORD)> func = [&pImport](DWORD l) {
        if (pImport != NULL)
        {
            DisassembleToken(pImport, l);
        }
        else
        {
            printf("TOKEN %x", l);
        }
    };
    position = DisplayILOperation(indentCount, pBuffer, position, func);
    return std::make_tuple(position, indentCount);
}

ULONG DisplayILOperation(const UINT indentCount, BYTE *pBuffer, ULONG position, std::function<void(DWORD)>& disassembleTokenFunc)
{
    printf("%*sIL_%04x: ", indentCount, "", position);
    unsigned int c = ReadOpcode(pBuffer, position);
    OpCode opcode = opcodes[c];
    printf("%s ", opcode.name);

    switch (opcode.args)
    {
    case InlineNone: break;

    case ShortInlineVar:
        printf("VAR OR ARG %d", ReadData<BYTE>(pBuffer, /* byref */ position)); break;
    case InlineVar:
        printf("VAR OR ARG %d", ReadData<WORD>(pBuffer, /* byref */ position)); break;
    case InlineI:
        printf("%d", ReadData<LONG>(pBuffer, /* byref */ position));
        break;
    case InlineR:
        printf("%f", ReadData<double>(pBuffer, /* byref */ position));
        break;
    case InlineBrTarget:
        printf("IL_%04x", ReadData<LONG>(pBuffer, /* byref */ position) + position); break;
    case ShortInlineBrTarget:
        printf("IL_%04x", ReadData<BYTE>(pBuffer, /* byref */ position) + position); break;
    case InlineI8:
        printf("%ld", ReadData<__int64>(pBuffer, /* byref */ position)); break;

    case InlineMethod:
    case InlineField:
    case InlineType:
    case InlineTok:
    case InlineSig:
    case InlineString:
    {
        LONG l = ReadData<LONG>(pBuffer, /* byref */ position);
        disassembleTokenFunc(l);
        break;
    }

    case InlineSwitch:
    {
        LONG cases = ReadData<LONG>(pBuffer, /* byref */ position);
        LONG* pArray = new LONG[cases];
        LONG i = 0;
        for (i = 0;i<cases;i++)
        {
            pArray[i] = ReadData<LONG>(pBuffer, /* byref */ position);
        }
        printf("(");
        for (i = 0;i<cases;i++)
        {
            if (i != 0)
                printf(", ");
            printf("IL_%04x", pArray[i] + position);
        }
        printf(")");
        delete[] pArray;
        break;
    }
    case ShortInlineI:
        printf("%d", ReadData<BYTE>(pBuffer, /* byref */ position)); break;
    case ShortInlineR:
        printf("%f", ReadData<float>(pBuffer, /* byref */ position)); break;
    default: printf("Error, unexpected opcode type\n"); break;
    }
    return position;
}

DWORD_PTR GetObj(DacpObjectData& tokenArray, UINT item)
{
    if (item < tokenArray.dwNumComponents)
    {
        DWORD_PTR dwAddr = (DWORD_PTR) (tokenArray.ArrayDataPtr + tokenArray.dwComponentSize*item);
        DWORD_PTR objPtr;
        if (SUCCEEDED(MOVE(objPtr, dwAddr)))
        {
            return objPtr;
        }
    }
    return NULL;
}


void DisassembleToken(DacpObjectData& tokenArray,
                      DWORD token)
{    
    switch (TypeFromToken(token))
    {
    default:
        printf("<unknown token type (token=%08x)>", token);
        break;

    case mdtTypeDef:
        {
            DWORD_PTR runtimeTypeHandle = GetObj(tokenArray, RidFromToken(token));

            DWORD_PTR runtimeType = NULL;
            MOVE(runtimeType, runtimeTypeHandle + sizeof(DWORD_PTR));

            int offset = GetObjFieldOffset(runtimeType, W("m_handle"));

            DWORD_PTR methodTable = NULL;
            MOVE(methodTable, runtimeType + offset);

            if (NameForMT_s(methodTable, g_mdName,mdNameLen))
            {
                printf("%x \"%S\"", token, g_mdName);
            }
            else
            {
                printf("<invalid MethodTable>");
            }
        }
        break;

    case mdtSignature:
    case mdtTypeRef:
        {
            printf ("%x (%p)", token, SOS_PTR(GetObj(tokenArray, RidFromToken(token))));
        }
        break;

    case mdtFieldDef:
        {
            printf ("%x (%p)", token, SOS_PTR(GetObj(tokenArray, RidFromToken(token))));
        }
        break;

    case mdtMethodDef:
        {
            CLRDATA_ADDRESS runtimeMethodHandle = GetObj(tokenArray, RidFromToken(token));            
            int offset = GetObjFieldOffset(runtimeMethodHandle, W("m_value"));

            TADDR runtimeMethodInfo = NULL;
            MOVE(runtimeMethodInfo, runtimeMethodHandle+offset);

            offset = GetObjFieldOffset(runtimeMethodInfo, W("m_handle"));

            TADDR methodDesc = NULL;
            MOVE(methodDesc, runtimeMethodInfo+offset);

            NameForMD_s((DWORD_PTR)methodDesc, g_mdName, mdNameLen);
            printf ("%x %S", token, g_mdName);
        }
        break;

    case mdtMemberRef:
        {
            printf ("%x (%p)", token, SOS_PTR(GetObj(tokenArray, RidFromToken(token))));
        }
        break;
    case mdtString:
        {
            DWORD_PTR strObj = GetObj(tokenArray, RidFromToken(token));
            printf ("%x \"", token);
            StringObjectContent (strObj, FALSE, 40);
            printf ("\"");
        }
        break;
    }
}

// Similar to the function above. TODO: factor them together before checkin.
void DecodeDynamicIL(BYTE *data, ULONG Size, DacpObjectData& tokenArray)
{
    // There is no header for this dynamic guy.
    ULONG position = 0;
    BYTE *pBuffer = data;

    // At this time no exception information will be displayed (fix soon)
    UINT indentCount = 0;
    ULONG endCodePosition = Size;
    while(position < endCodePosition)
    {
        std::function<void(DWORD)> func = [&tokenArray](DWORD l) {
            DisassembleToken(tokenArray, l);
        };
        position = DisplayILOperation(indentCount, pBuffer, position, func);
        printf("\n");
    }
}



/******************************************************************************/
// CQuickBytes utilities
static char* asString(CQuickBytes *out) {
    SIZE_T oldSize = out->Size();
    out->ReSize(oldSize + 1);
    char* cur = &((char*) out->Ptr())[oldSize]; 
    *cur = 0;   
    out->ReSize(oldSize);           // Don't count the null character
    return((char*) out->Ptr()); 
}

static void appendStr(CQuickBytes *out, const char* str, unsigned len=-1) {
    if(len == (unsigned)(-1)) len = (unsigned)strlen(str); 
    SIZE_T oldSize = out->Size();
    out->ReSize(oldSize + len);
    char* cur = &((char*) out->Ptr())[oldSize]; 
    memcpy(cur, str, len);  
        // Note no trailing null!   
}

static void appendStr(CQuickBytes *out, const WCHAR* str, unsigned len=-1) {
    if(len == (unsigned)(-1)) len = (unsigned)_wcslen(str); 

    int acpSize = WideCharToMultiByte(CP_ACP, 0, str, len, NULL, 0, NULL, NULL);
    char *pAcpString = (char*)_alloca(acpSize + 1);
    WideCharToMultiByte(CP_ACP, 0, str, len, pAcpString, acpSize, NULL, NULL);
    pAcpString[acpSize] = '\0';
    appendStr(out, pAcpString, acpSize);
}

static void appendChar(CQuickBytes *out, char chr) {
    SIZE_T oldSize = out->Size();
    out->ReSize(oldSize + 1); 
    ((char*) out->Ptr())[oldSize] = chr; 
        // Note no trailing null!   
}

static void insertStr(CQuickBytes *out, const char* str) {
    unsigned len = (unsigned)strlen(str); 
    SIZE_T oldSize = out->Size();
    out->ReSize(oldSize + len); 
    char* cur = &((char*) out->Ptr())[len];
    memmove(cur,out->Ptr(),oldSize);
    memcpy(out->Ptr(), str, len);  
        // Note no trailing null!   
}

static void appendStrNum(CQuickBytes *out, int num) {
    char buff[16];  
    sprintf_s(buff, COUNTOF(buff), "%d", num);   
    appendStr(out, buff);   
}


//PrettyPrinting type names
PCCOR_SIGNATURE PrettyPrintType(
    PCCOR_SIGNATURE typePtr,            // type to convert,     
    CQuickBytes *out,                   // where to put the pretty printed string   
    IMetaDataImport *pIMD,           // ptr to IMetaDataImport class with ComSig
    DWORD formatFlags /*= formatILDasm*/)
{
    mdToken  tk;    
    const char* str;    
    int typ;
    CQuickBytes tmp;
    CQuickBytes Appendix;
    BOOL Reiterate;
    int n;

    do {
        Reiterate = FALSE;
        switch(typ = *typePtr++) {    
            case ELEMENT_TYPE_VOID          :   
                str = "void"; goto APPEND;  
            case ELEMENT_TYPE_BOOLEAN       :   
                str = "bool"; goto APPEND;  
            case ELEMENT_TYPE_CHAR          :   
                str = "char"; goto APPEND; 
            case ELEMENT_TYPE_I1            :   
                str = "int8"; goto APPEND;  
            case ELEMENT_TYPE_U1            :   
                str = "uint8"; goto APPEND; 
            case ELEMENT_TYPE_I2            :   
                str = "int16"; goto APPEND; 
            case ELEMENT_TYPE_U2            :   
                str = "uint16"; goto APPEND;    
            case ELEMENT_TYPE_I4            :   
                str = "int32"; goto APPEND; 
            case ELEMENT_TYPE_U4            :   
                str = "uint32"; goto APPEND;    
            case ELEMENT_TYPE_I8            :   
                str = "int64"; goto APPEND; 
            case ELEMENT_TYPE_U8            :   
                str = "uint64"; goto APPEND;    
            case ELEMENT_TYPE_R4            :   
                str = "float32"; goto APPEND;   
            case ELEMENT_TYPE_R8            :   
                str = "float64"; goto APPEND;   
            case ELEMENT_TYPE_U             :   
                str = "native uint"; goto APPEND;   
            case ELEMENT_TYPE_I             :   
                str = "native int"; goto APPEND;    
            case ELEMENT_TYPE_OBJECT        :   
                str = "object"; goto APPEND;    
            case ELEMENT_TYPE_STRING        :   
                str = "string"; goto APPEND;    
            case ELEMENT_TYPE_TYPEDBYREF        :   
                str = "typedref"; goto APPEND;    
            APPEND: 
                appendStr(out, (char*)str);
                break;  

            case ELEMENT_TYPE_VALUETYPE    :   
                if ((formatFlags & FormatKwInNames) != 0) 
                    str = "valuetype ";   
                else str = "";
                goto DO_CLASS;  
            case ELEMENT_TYPE_CLASS         :   
                if ((formatFlags & FormatKwInNames) != 0) 
                    str = "class "; 
                else str = "";
                goto DO_CLASS;  

            DO_CLASS:
                appendStr(out, (char*)str);
                typePtr += CorSigUncompressToken(typePtr, &tk); 
                if(IsNilToken(tk))
                {
                    appendStr(out, "[ERROR! NIL TOKEN]");
                }
                else PrettyPrintClass(out, tk, pIMD, formatFlags);
                break;  

            case ELEMENT_TYPE_SZARRAY    :   
                insertStr(&Appendix,"[]");
                Reiterate = TRUE;
                break;
            
            case ELEMENT_TYPE_ARRAY       :   
                {   
                typePtr = PrettyPrintType(typePtr, out, pIMD, formatFlags);
                unsigned rank = CorSigUncompressData(typePtr);  
                    // <TODO> what is the syntax for the rank 0 case? </TODO> 
                if (rank == 0) {
                    appendStr(out, "[BAD: RANK == 0!]");
                }
                else {
                    _ASSERTE(rank != 0);
#ifdef _PREFAST_
#pragma warning(push)
#pragma warning(disable:22009) // "Suppress PREfast warnings about integer overflow"
// PREFAST warns about using _alloca in a loop.  However when we're in this switch case we do NOT
// set Reiterate to true, so we only execute through the loop once!
#pragma warning(disable:6263) // "Suppress PREfast warnings about stack overflow due to _alloca in a loop."
#endif
                    int* lowerBounds = (int*) _alloca(sizeof(int)*2*rank);
                    int* sizes       = &lowerBounds[rank];  
                    memset(lowerBounds, 0, sizeof(int)*2*rank); 
                    
                    unsigned numSizes = CorSigUncompressData(typePtr);  
                    _ASSERTE(numSizes <= rank);
                        unsigned i;
                    for(i =0; i < numSizes; i++)
                        sizes[i] = CorSigUncompressData(typePtr);   
                    
                    unsigned numLowBounds = CorSigUncompressData(typePtr);  
                    _ASSERTE(numLowBounds <= rank); 
                    for(i = 0; i < numLowBounds; i++)   
                        typePtr+=CorSigUncompressSignedInt(typePtr,&lowerBounds[i]); 
                    
                    appendChar(out, '[');    
                    if (rank == 1 && numSizes == 0 && numLowBounds == 0)
                        appendStr(out, "...");  
                    else {
                        for(i = 0; i < rank; i++)   
                        {   
                            //if (sizes[i] != 0 || lowerBounds[i] != 0)   
                            {   
                                if (i < numSizes && lowerBounds[i] == 0)
                                    appendStrNum(out, sizes[i]);    
                                else    
                                {   
                                    if(i < numLowBounds)
                                    {
                                        appendStrNum(out, lowerBounds[i]);  
                                        appendStr(out, "...");  
                                        if (/*sizes[i] != 0 && */i < numSizes)  
                                            appendStrNum(out, lowerBounds[i] + sizes[i] - 1);   
                                    }
                                }   
                            }   
                            if (i < rank-1) 
                                appendChar(out, ',');    
                        }   
                    }
                    appendChar(out, ']'); 
#ifdef _PREFAST_
#pragma warning(pop)
#endif
                }
                } break;    

            case ELEMENT_TYPE_VAR        :   
                appendChar(out, '!');
                n  = CorSigUncompressData(typePtr);
                appendStrNum(out, n);
                break;

            case ELEMENT_TYPE_MVAR        :   
                appendChar(out, '!');    
                appendChar(out, '!');    
                n  = CorSigUncompressData(typePtr);
                appendStrNum(out, n);
                break;

            case ELEMENT_TYPE_FNPTR :   
                appendStr(out, "method ");  
                appendStr(out, "METHOD"); // was: typePtr = PrettyPrintSignature(typePtr, 0x7FFF, "*", out, pIMD, NULL);
                break;

            case ELEMENT_TYPE_GENERICINST :
            {
              typePtr = PrettyPrintType(typePtr, out, pIMD, formatFlags);
              if ((formatFlags & FormatSignature) == 0)
                  break;

              if ((formatFlags & FormatAngleBrackets) != 0)
                  appendStr(out, "<");
              else
                  appendStr(out,"[");
              unsigned numArgs = CorSigUncompressData(typePtr);    
              bool needComma = false;
              while(numArgs--)
              {
                  if (needComma)
                      appendChar(out, ',');
                  typePtr = PrettyPrintType(typePtr, out, pIMD, formatFlags);
                  needComma = true;
              }
              if ((formatFlags & FormatAngleBrackets) != 0)
                  appendStr(out, ">");
              else
                  appendStr(out,"]");
              break;
            }

            case ELEMENT_TYPE_PINNED:
                str = " pinned"; goto MODIFIER;
            case ELEMENT_TYPE_PTR           :
                str = "*"; goto MODIFIER;
            case ELEMENT_TYPE_BYREF         :
                str = "&"; goto MODIFIER;
            MODIFIER:
                insertStr(&Appendix, str);
                Reiterate = TRUE;
                break;
            case ELEMENT_TYPE_CMOD_REQD:
                appendStr(out, " mod req ");
                typePtr += CorSigUncompressToken(typePtr, &tk); 
                if(IsNilToken(tk))
                {
                    appendStr(out, "[ERROR! NIL TOKEN]");
                }
                else PrettyPrintClass(out, tk, pIMD, formatFlags);
                Reiterate = TRUE;
                break;
            case ELEMENT_TYPE_CMOD_OPT:
                appendStr(out, " mod opt ");
                typePtr += CorSigUncompressToken(typePtr, &tk); 
                if(IsNilToken(tk))
                {
                    appendStr(out, "[ERROR! NIL TOKEN]");
                }
                else PrettyPrintClass(out, tk, pIMD, formatFlags);
                Reiterate = TRUE;
                break;


            default:    
            case ELEMENT_TYPE_SENTINEL      :   
            case ELEMENT_TYPE_END           :   
                //_ASSERTE(!"Unknown Type");
                if(typ)
                {
                    char sz[64];
                    sprintf_s(sz,COUNTOF(sz),"/* UNKNOWN TYPE (0x%X)*/",typ);
                    appendStr(out, sz);
                }
                break;  
        } // end switch
    } while(Reiterate);
    if (Appendix.Size() > 0)
        appendStr(out,asString(&Appendix));

    return(typePtr);    
}

// Protection against null names, used by ILDASM/SOS
const WCHAR *const szStdNamePrefix[] = {W("MO"),W("TR"),W("TD"),W(""),W("FD"),W(""),W("MD"),W(""),W("PA"),W("II"),W("MR"),W(""),W("CA"),W(""),W("PE"),W(""),W(""),W("SG"),W(""),W(""),W("EV"),
W(""),W(""),W("PR"),W(""),W(""),W("MOR"),W("TS"),W(""),W(""),W(""),W(""),W("AS"),W(""),W(""),W("AR"),W(""),W(""),W("FL"),W("ET"),W("MAR")};

#define MAKE_NAME_IF_NONE(psz, tk) { if(!(psz && *psz)) { WCHAR* sz = (WCHAR*)_alloca(16 * sizeof(WCHAR)); \
swprintf_s(sz,16,W("$%s$%X"),szStdNamePrefix[tk>>24],tk&0x00FFFFFF); psz = sz; } }

const char* PrettyPrintClass(
    CQuickBytes *out,                   // where to put the pretty printed string   
    mdToken tk,                         // The class token to look up 
    IMetaDataImport *pIMD,           // ptr to IMetaDataImport class with ComSig
    DWORD formatFlags /*= formatILDasm*/)
{
#define MAX_TYPE_NAME_LEN MAX_CLASSNAME_LENGTH + MAX_CLASSNAME_LENGTH + 1
    WCHAR nameComplete[MAX_TYPE_NAME_LEN];
    if(tk == mdTokenNil)  // Zero resolution scope for "somewhere here" TypeRefs
    {
        appendStr(out,"[*]");
        return(asString(out));
    }
    if (!pIMD->IsValidToken(tk))
    {
        char str[1024];
        sprintf_s(str,COUNTOF(str)," [ERROR: INVALID TOKEN 0x%8.8X] ",tk);
        appendStr(out, str);
        return(asString(out));
    }
    switch (TypeFromToken(tk))
    {
        case mdtTypeRef:
        case mdtTypeDef:
            {
                WCHAR *name = NULL;
                ULONG unused;
                mdToken tkEncloser = mdTokenNil;
                
                if (TypeFromToken(tk) == mdtTypeRef)
                {
                    if (((formatFlags & FormatAssembly) && FAILED(pIMD->GetTypeRefProps(tk, &tkEncloser, nameComplete, MAX_TYPE_NAME_LEN, &unused))))
                    {
                        char str[1024];
                        sprintf_s(str, COUNTOF(str), " [ERROR: Invalid TypeRef record 0x%8.8X] ", tk);
                        appendStr(out, str);
                        return asString(out);
                    }
                }
                else 
                {
                    DWORD typeDefFlags = 0;
                    mdToken tkExtends;

                    if (FAILED(pIMD->GetTypeDefProps(tk, nameComplete, MAX_TYPE_NAME_LEN, &unused, &typeDefFlags, &tkExtends)))
                    {
                        char str[1024];
                        sprintf_s(str, COUNTOF(str), " [ERROR: Invalid TypeDef record 0x%8.8X] ", tk);
                        appendStr(out, str);
                        return asString(out);
                    }

                    if (IsTdNested(typeDefFlags))
                    {
                        if (FAILED(pIMD->GetNestedClassProps(tk, &tkEncloser)))
                        {
                            tkEncloser = mdTypeDefNil;
                        }
                    }
                }

                if ((formatFlags & FormatNamespace) == 0)
                {
                    tkEncloser = mdTypeDefNil;
                    auto nameLen = _wcslen(nameComplete);
                    name = nameComplete;

                    for (decltype(nameLen) index = 0; index < nameLen; index++)
                    {
                        if (nameComplete[index] == '.')
                        {
                            name = nameComplete + index + 1;
                        }
                    }
                }
                else
                {
                    name = nameComplete;
                }

                MAKE_NAME_IF_NONE(name,tk);
                if((tkEncloser == mdTokenNil) || RidFromToken(tkEncloser))
                {
                    if (TypeFromToken(tkEncloser) == mdtTypeRef || TypeFromToken(tkEncloser) == mdtTypeDef)
                    {
                        PrettyPrintClass(out,tkEncloser,pIMD, formatFlags);
                        if (formatFlags & FormatSlashSep)
                            appendChar(out, '/');    
                        else
                            appendChar(out, '+');
                        //nameSpace = ""; //don't print namespaces for nested classes!
                    }
                    else if (formatFlags & FormatAssembly)
                    {
                        PrettyPrintClass(out,tkEncloser,pIMD, formatFlags);
                    }
                }
                appendStr(out, name);
            }
            break;

        case mdtAssemblyRef:
            {
                LPCSTR szName = NULL;
                ULONG unused;
                ToRelease<IMetaDataAssemblyImport> pAsmImport;
                if (SUCCEEDED(pIMD->QueryInterface(IID_IMetaDataAssemblyImport, (LPVOID *)&pAsmImport)))
                {
                    if(SUCCEEDED(pAsmImport->GetAssemblyRefProps(tk,NULL,NULL,nameComplete, MAX_TYPE_NAME_LEN,&unused,NULL, NULL, NULL, NULL)))
                    {
                        appendChar(out, '[');    
                        appendStr(out, nameComplete);
                        appendChar(out, ']');    
                    }
                }
            }
            break;
        case mdtAssembly:
            {
                ULONG unused;
                ToRelease<IMetaDataAssemblyImport> pAsmImport;
                if (SUCCEEDED(pIMD->QueryInterface(IID_IMetaDataAssemblyImport, (LPVOID *)&pAsmImport)))
                {
                    if(SUCCEEDED(pAsmImport->GetAssemblyProps(tk,NULL,NULL,NULL,nameComplete, MAX_TYPE_NAME_LEN,&unused,NULL, NULL)))
                    {
                        appendChar(out, '[');    
                        appendStr(out, nameComplete);
                        appendChar(out, ']');    
                    }
                }
            }
            break;
        case mdtModuleRef:
            {
                ULONG unused;
                if(SUCCEEDED(pIMD->GetModuleRefProps(tk,nameComplete, MAX_TYPE_NAME_LEN, &unused)))
                {
                    appendChar(out, '[');    
                    appendStr(out, ".module ");
                    appendStr(out, nameComplete);
                    appendChar(out, ']');    
                }
            }
            break;

        case mdtTypeSpec:
            {
                ULONG cSig;
                PCCOR_SIGNATURE sig;
                if (FAILED(pIMD->GetTypeSpecFromToken(tk, &sig, &cSig)))
                {
                    char str[128];
                    sprintf_s(str, COUNTOF(str), " [ERROR: Invalid token 0x%8.8X] ", tk);
                    appendStr(out, str);
                }
                else
                {
                    PrettyPrintType(sig, out, pIMD, formatFlags);
                }
            }
            break;

        case mdtModule:
            break;
        
        default:
            {
                char str[128];
                sprintf_s(str,COUNTOF(str)," [ERROR: INVALID TOKEN TYPE 0x%8.8X] ",tk);
                appendStr(out, str);
            }
    }
    return(asString(out));
}

// This function takes a module and a token and prints the representation in the mdName buffer.
void PrettyPrintClassFromToken(
    TADDR moduleAddr,                   // the module containing the token
    mdToken tok,                        // the class token to look up
    __out_ecount(cbName) WCHAR* mdName, // where to put the pretty printed string
    size_t cbName,                      // the capacity of the buffer
    DWORD formatFlags /*= FormatCSharp*/)
{
    // set the default value
    swprintf_s(mdName, cbName, W("token_0x%8.8X"), tok);

    DacpModuleData dmd;
    if (dmd.Request(g_sos, TO_CDADDR(moduleAddr)) != S_OK)
        return;

    ToRelease<IMetaDataImport> pImport(MDImportForModule(&dmd));

    CQuickBytes qb;
    PrettyPrintClass(&qb, tok, pImport, formatFlags);
    MultiByteToWideChar (CP_ACP, 0, asString(&qb), -1, mdName, (int) cbName);
}
