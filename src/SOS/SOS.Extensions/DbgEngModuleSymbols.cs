// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Extensions
{
    internal sealed class DbgEngModuleSymbols
    {
        private readonly DebuggerServices.IDebugAdvancedOperations _advanced;
        private readonly DebuggerServices.IDebugSymbolsOperations _symbols;

        internal DbgEngModuleSymbols(
            DebuggerServices.IDebugAdvancedOperations advanced,
            DebuggerServices.IDebugSymbolsOperations symbols)
        {
            _advanced = advanced;
            _symbols = symbols;
        }

        internal HResult GetSymbolByOffset(ulong address, out string symbol, out ulong displacement)
        {
            symbol = null;
            displacement = 0;

            HResult hr = GetPublicSymbolName(address, out string publicName);
            if (hr != HResult.S_OK)
            {
                return hr;
            }

            // Displacement may not exactly match the public symbol, but is correct where it matters
            // for our validated use cases.
            hr = _symbols.GetNameByOffsetWide(address, out ulong offset);
            if (hr != HResult.S_OK || offset > address)
            {
                return hr != HResult.S_OK ? hr : HResult.E_FAIL;
            }
            if (publicName.Length == 0)
            {
                // Export-only symbols can return an empty name at interior addresses. Query their base instead.
                hr = GetPublicSymbolName(address - offset, out publicName);
                if (hr != HResult.S_OK || publicName.Length == 0)
                {
                    return hr != HResult.S_OK ? hr : HResult.E_FAIL;
                }
            }

            symbol = publicName;
            displacement = offset;
            return HResult.S_OK;
        }

        private HResult GetPublicSymbolName(ulong address, out string symbol)
        {
            const uint SymTagPublicSymbol = 10;

            symbol = null;
            HResult hr = _advanced.GetSymbolInformation(
                DEBUG_SYMINFO.GET_SYMBOL_NAME_BY_OFFSET_AND_TAG_WIDE,
                address,
                SymTagPublicSymbol,
                null,
                out int size);
            if (hr != HResult.S_OK && hr != HResult.S_FALSE)
            {
                return hr;
            }
            if (size < sizeof(char) || size % sizeof(char) != 0)
            {
                return HResult.E_FAIL;
            }
            byte[] buffer = new byte[size];
            hr = _advanced.GetSymbolInformation(
                DEBUG_SYMINFO.GET_SYMBOL_NAME_BY_OFFSET_AND_TAG_WIDE,
                address,
                SymTagPublicSymbol,
                buffer,
                out size);
            if (hr == HResult.S_OK)
            {
                symbol = Encoding.Unicode.GetString(buffer, 0, size).TrimEnd('\0');
            }
            return hr;
        }
    }
}
