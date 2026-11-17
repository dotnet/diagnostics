// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Extensions;
using SOS.Hosting.DbgEng.Interop;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class DebuggerServicesTests
    {
        private const ulong SymbolAddress = 0x7ff000010000;
        private const string PublicName = "StablePublicName_\u03bb";

        [Theory]
        [InlineData(0UL, 1)]
        [InlineData(0x35UL, 2)]
        public void PublicSymbolLookupPreservesDisplacement(ulong offset, int nullTerminators)
        {
            DebuggerServices.IDebugAdvancedOperations advanced = CreateAdvanced(_ => PublicName, nullTerminators);
            DebuggerServices.IDebugSymbolsOperations symbols = CreateSymbols();

            HResult hr = DebuggerServices.GetPublicSymbolByOffset(advanced, symbols, 3, SymbolAddress + offset, out string name, out ulong displacement);

            Assert.Equal(HResult.S_OK, (int)hr);
            Assert.Equal(PublicName, name);
            Assert.Equal(offset, displacement);
        }

        [Fact]
        public void ExportLookupRetriesAtSymbolBase()
        {
            List<ulong> addresses = new();
            DebuggerServices.IDebugAdvancedOperations advanced = CreateAdvanced(address => {
                addresses.Add(address);
                return address == SymbolAddress ? PublicName : string.Empty;
            });
            DebuggerServices.IDebugSymbolsOperations symbols = CreateSymbols(exportDisplacement: 0x35);

            HResult hr = DebuggerServices.GetPublicSymbolByOffset(advanced, symbols, 3, SymbolAddress + 0x35, out string name, out ulong displacement);

            Assert.Equal(HResult.S_OK, (int)hr);
            Assert.Equal(PublicName, name);
            Assert.Equal(0x35UL, displacement);
            Assert.Contains(SymbolAddress + 0x35, addresses);
            Assert.Contains(SymbolAddress, addresses);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(unchecked((int)0x80004005))]
        public void AddressResolutionFailureDoesNotReturnAPartialResult(int result)
        {
            DebuggerServices.IDebugAdvancedOperations advanced = CreateAdvanced(_ => PublicName);
            DebuggerServices.IDebugSymbolsOperations symbols = CreateSymbols(lookupResult: result);

            HResult hr = DebuggerServices.GetPublicSymbolByOffset(advanced, symbols, 3, SymbolAddress + 1, out string name, out ulong displacement);

            Assert.Equal(result, (int)hr);
            Assert.Null(name);
            Assert.Equal(0UL, displacement);
        }

        [Fact]
        public void PublicNameLookupFailureIsPropagated()
        {
            IDebugAdvanced2 advancedProxy = CreateProxy<IDebugAdvanced2>((_, arguments) => {
                arguments[5] = 0;
                arguments[8] = 0;
                return (int)HResult.E_FAIL;
            });
            DebuggerServices.IDebugAdvancedOperations advanced = new DebugAdvancedOperations(advancedProxy);
            DebuggerServices.IDebugSymbolsOperations symbols = CreateSymbols();

            HResult hr = DebuggerServices.GetPublicSymbolByOffset(advanced, symbols, 3, SymbolAddress, out string name, out ulong displacement);

            Assert.Equal(HResult.E_FAIL, (int)hr);
            Assert.Null(name);
            Assert.Equal(0UL, displacement);
        }

        [Fact]
        public void SymbolAfterAddressDoesNotUnderflowDisplacement()
        {
            DebuggerServices.IDebugAdvancedOperations advanced = CreateAdvanced(_ => PublicName);
            DebuggerServices.IDebugSymbolsOperations symbols = CreateSymbols();

            HResult hr = DebuggerServices.GetPublicSymbolByOffset(advanced, symbols, 3, SymbolAddress - 1, out string name, out ulong displacement);

            Assert.Equal(HResult.E_FAIL, (int)hr);
            Assert.Null(name);
            Assert.Equal(0UL, displacement);
        }

        private static DebuggerServices.IDebugAdvancedOperations CreateAdvanced(Func<ulong, string> getName, int nullTerminators = 2)
        {
            IDebugAdvanced2 proxy = CreateProxy<IDebugAdvanced2>((method, arguments) => {
                Assert.Equal(nameof(IDebugAdvanced2.GetSymbolInformation), method.Name);
                Assert.Equal(DEBUG_SYMINFO.GET_SYMBOL_NAME_BY_OFFSET_AND_TAG_WIDE, arguments[0]);
                Assert.Equal(10U, arguments[2]);

                byte[] name = Encoding.Unicode.GetBytes(getName((ulong)arguments[1]) + new string('\0', nullTerminators));
                arguments[5] = name.Length;
                arguments[8] = 0;
                if (arguments[3] is byte[] buffer)
                {
                    name.CopyTo(buffer, 0);
                    return 0;
                }
                return 1;
            });
            return new DebugAdvancedOperations(proxy);
        }

        private static DebuggerServices.IDebugSymbolsOperations CreateSymbols(int lookupResult = 0, ulong? exportDisplacement = null)
        {
            IDebugSymbols3 proxy = CreateProxy<IDebugSymbols3>((method, arguments) => {
                switch (method.Name)
                {
                    case nameof(IDebugSymbols3.GetModuleNameStringWide):
                        Assert.Equal(DEBUG_MODNAME.MODULE, arguments[0]);
                        Assert.Equal(3U, arguments[1]);
                        arguments[5] = 11U;
                        if (arguments[3] is StringBuilder buffer)
                        {
                            buffer.Append("testmodule");
                        }
                        return 0;

                    case nameof(IDebugSymbols3.GetOffsetByNameWide):
                        Assert.Equal($"testmodule!{PublicName}", arguments[0]);
                        arguments[1] = SymbolAddress;
                        return lookupResult;

                    case nameof(IDebugSymbols3.GetNameByOffsetWide):
                        Assert.NotNull(exportDisplacement);
                        arguments[3] = 1U;
                        arguments[4] = exportDisplacement.Value;
                        return 0;

                    default:
                        throw new NotSupportedException(method.Name);
                }
            });
            return new DebugSymbolsOperations(proxy);
        }

        private static T CreateProxy<T>(Func<MethodInfo, object[], object> handler) where T : class
        {
            T proxy = DispatchProxy.Create<T, DbgEngProxy>();
            ((DbgEngProxy)(object)proxy).Handler = handler;
            return proxy;
        }

        public class DbgEngProxy : DispatchProxy
        {
            public Func<MethodInfo, object[], object> Handler { get; set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args) => Handler(targetMethod, args);
        }

        private sealed class DebugAdvancedOperations : DebuggerServices.IDebugAdvancedOperations
        {
            private readonly IDebugAdvanced2 _advanced;

            public DebugAdvancedOperations(IDebugAdvanced2 advanced)
            {
                _advanced = advanced;
            }

            public HResult GetSymbolInformation(DEBUG_SYMINFO which, ulong arg64, uint arg32, byte[] buffer, out int infoSize) =>
                _advanced.GetSymbolInformation(which, arg64, arg32, buffer, buffer?.Length ?? 0, out infoSize, null, 0, out _);
        }

        private sealed class DebugSymbolsOperations : DebuggerServices.IDebugSymbolsOperations
        {
            private readonly IDebugSymbols3 _symbols;

            public DebugSymbolsOperations(IDebugSymbols3 symbols)
            {
                _symbols = symbols;
            }

            public HResult GetNameByOffsetWide(ulong offset, out ulong displacement) =>
                _symbols.GetNameByOffsetWide(offset, null, 0, out _, out displacement);

            public HResult GetOffsetByNameWide(string symbol, out ulong offset) =>
                _symbols.GetOffsetByNameWide(symbol, out offset);

            public HResult GetModuleNameStringWide(
                DEBUG_MODNAME which,
                uint index,
                ulong imageBase,
                StringBuilder buffer,
                int bufferSize,
                out uint nameSize) =>
                _symbols.GetModuleNameStringWide(which, index, imageBase, buffer, bufferSize, out nameSize);
        }
    }
}
