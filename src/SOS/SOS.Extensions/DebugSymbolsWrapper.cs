// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Extensions
{
    internal sealed unsafe class DebugSymbolsWrapper : CallableCOMWrapper, DebuggerServices.IDebugSymbolsOperations
    {
        private IntPtr* VTable => *(IntPtr**)Self;

        public DebugSymbolsWrapper(IntPtr symbols)
            : base(typeof(IDebugSymbols5).GUID, symbols)
        {
        }

        // These IDebugSymbols slots include IUnknown and are inherited by IDebugSymbols5.
        public HResult GetModuleParameters(ulong imageBase, out DEBUG_MODULE_PARAMETERS parameters)
        {
            parameters = default;
            fixed (DEBUG_MODULE_PARAMETERS* parametersPtr = &parameters)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, uint, ulong*, uint, DEBUG_MODULE_PARAMETERS*, int>)VTable[17])(
                    Self, 1, &imageBase, 0, parametersPtr);
            }
        }

        public HResult Reload(string module)
        {
            IntPtr modulePtr = Marshal.StringToHGlobalAnsi(module);
            try
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)VTable[39])(Self, modulePtr);
            }
            finally
            {
                Marshal.FreeHGlobal(modulePtr);
            }
        }

        public HResult GetNameByOffset(ulong offset)
        {
            // Only the lookup side effect is needed to force symbol loading.
            return ((delegate* unmanaged[Stdcall]<IntPtr, ulong, byte*, uint, uint*, ulong*, int>)VTable[7])(
                Self, offset, null, 0, null, null);
        }

        public HResult GetNameByOffsetWide(ulong offset, out ulong displacement)
        {
            displacement = 0;
            fixed (ulong* displacementPtr = &displacement)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, ulong, char*, int, uint*, ulong*, int>)VTable[60])(
                    Self, offset, null, 0, null, displacementPtr);
            }
        }

        public HResult GetOffsetByNameWide(string symbol, out ulong offset)
        {
            offset = 0;
            IntPtr symbolPtr = Marshal.StringToHGlobalUni(symbol);
            try
            {
                fixed (ulong* offsetPtr = &offset)
                {
                    return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, ulong*, int>)VTable[61])(
                        Self, symbolPtr, offsetPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(symbolPtr);
            }
        }

        public HResult GetModuleNameStringWide(
            DEBUG_MODNAME which,
            uint index,
            ulong imageBase,
            System.Text.StringBuilder buffer,
            int bufferSize,
            out uint nameSize)
        {
            nameSize = 0;
            char[] chars = buffer is null || bufferSize == 0 ? null : new char[bufferSize];
            fixed (char* bufferPtr = chars)
            fixed (uint* nameSizePtr = &nameSize)
            {
                HResult hr = ((delegate* unmanaged[Stdcall]<IntPtr, DEBUG_MODNAME, uint, ulong, char*, int, uint*, int>)VTable[89])(
                    Self, which, index, imageBase, bufferPtr, bufferSize, nameSizePtr);
                if (hr == HResult.S_OK && chars is not null)
                {
                    int length = Array.IndexOf(chars, '\0');
                    buffer.Append(chars, 0, length >= 0 ? length : chars.Length);
                }
                return hr;
            }
        }
    }
}
