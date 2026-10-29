// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Extensions
{
    internal sealed unsafe class DebugSymbolsWrapper : CallableCOMWrapper
    {
        private IntPtr* VTable => *(IntPtr**)Self;

        public DebugSymbolsWrapper(IntPtr symbols)
            : base(typeof(IDebugSymbols5).GUID, symbols)
        {
        }

        // These IDebugSymbols slots include IUnknown and are inherited by IDebugSymbols5.
        public HResult AddSymbolOptions(SYMOPT options)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, SYMOPT, int>)VTable[4])(Self, options);
        }

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
    }
}
