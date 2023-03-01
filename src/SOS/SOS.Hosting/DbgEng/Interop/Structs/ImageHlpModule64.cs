// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct IMAGEHLP_MODULE64
    {
        private const int MAX_PATH = 260;

        public uint SizeOfStruct;
        public ulong BaseOfImage;
        public uint ImageSize;
        public uint TimeDateStamp;
        public uint CheckSum;
        public uint NumSyms;
        public DEBUG_SYMTYPE SymType;
        private fixed char _ModuleName[32];
        private fixed char _ImageName[256];
        private fixed char _LoadedImageName[256];
        private fixed char _LoadedPdbName[256];
        public uint CVSig;
        public fixed char CVData[MAX_PATH * 3];
        public uint PdbSig;
        public Guid PdbSig70;
        public uint PdbAge;
        private uint _bPdbUnmatched; /* BOOL */
        private uint _bDbgUnmatched; /* BOOL */
        private uint _bLineNumbers; /* BOOL */
        private uint _bGlobalSymbols; /* BOOL */
        private uint _bTypeInfo; /* BOOL */
        private uint _bSourceIndexed; /* BOOL */
        private uint _bPublics; /* BOOL */

        public bool PdbUnmatched
        {
            get => _bPdbUnmatched != 0;
            set => _bPdbUnmatched = value ? 1U : 0U;
        }

        public bool DbgUnmatched
        {
            get => _bDbgUnmatched != 0;
            set => _bDbgUnmatched = value ? 1U : 0U;
        }

        public bool LineNumbers
        {
            get => _bLineNumbers != 0;
            set => _bLineNumbers = value ? 1U : 0U;
        }

        public bool GlobalSymbols
        {
            get => _bGlobalSymbols != 0;
            set => _bGlobalSymbols = value ? 1U : 0U;
        }

        public bool TypeInfo
        {
            get => _bTypeInfo != 0;
            set => _bTypeInfo = value ? 1U : 0U;
        }

        public bool SourceIndexed
        {
            get => _bSourceIndexed != 0;
            set => _bSourceIndexed = value ? 1U : 0U;
        }

        public bool Publics
        {
            get => _bPublics != 0;
            set => _bPublics = value ? 1U : 0U;
        }

        public string ModuleName
        {
            get {
                fixed (char* moduleNamePtr = _ModuleName)
                {
                    return Marshal.PtrToStringUni((IntPtr)moduleNamePtr, 32);
                }
            }
        }

        public string ImageName
        {
            get {
                fixed (char* imageNamePtr = _ImageName)
                {
                    return Marshal.PtrToStringUni((IntPtr)imageNamePtr, 256);
                }
            }
        }

        public string LoadedImageName
        {
            get {
                fixed (char* loadedImageNamePtr = _LoadedImageName)
                {
                    return Marshal.PtrToStringUni((IntPtr)loadedImageNamePtr, 256);
                }
            }
        }

        public string LoadedPdbName
        {
            get {
                fixed (char* loadedPdbNamePtr = _LoadedPdbName)
                {
                    return Marshal.PtrToStringUni((IntPtr)loadedPdbNamePtr, 256);
                }
            }
        }
    }
}
