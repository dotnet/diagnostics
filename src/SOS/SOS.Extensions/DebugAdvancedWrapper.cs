// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Extensions
{
    internal sealed unsafe class DebugAdvancedWrapper : CallableCOMWrapper, DebuggerServices.IDebugAdvancedOperations
    {
        private IntPtr* VTable => *(IntPtr**)Self;

        public DebugAdvancedWrapper(IntPtr advanced)
            : base(typeof(IDebugAdvanced2).GUID, advanced)
        {
        }

        public HResult GetSymbolInformation(
            DEBUG_SYMINFO which,
            ulong arg64,
            uint arg32,
            byte[] buffer,
            out int infoSize)
        {
            infoSize = 0;
            fixed (byte* bufferPtr = buffer)
            fixed (int* infoSizePtr = &infoSize)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, DEBUG_SYMINFO, ulong, uint, byte*, int, int*, char*, int, int*, int>)VTable[8])(
                    Self, which, arg64, arg32, bufferPtr, buffer?.Length ?? 0, infoSizePtr, null, 0, null);
            }
        }
    }
}
