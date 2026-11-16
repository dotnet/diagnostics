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
        public void PublicSymbolLookupUsesOrdinaryDisplacement(ulong offset, int nullTerminators)
        {
            IDebugAdvanced2 advanced = CreateAdvanced(_ => PublicName, nullTerminators);
            IDebugSymbols3 symbols = CreateSymbols(SymbolAddress + offset, offset);
            DbgEngModuleSymbols moduleSymbols = new(advanced, symbols);

            HResult hr = moduleSymbols.GetSymbolByOffset(SymbolAddress + offset, out string name, out ulong displacement);

            Assert.Equal(HResult.S_OK, (int)hr);
            Assert.Equal(PublicName, name);
            Assert.Equal(offset, displacement);
        }

        [Fact]
        public void ExportLookupRetriesAtSymbolBase()
        {
            List<ulong> addresses = new();
            IDebugAdvanced2 advanced = CreateAdvanced(address => {
                addresses.Add(address);
                return address == SymbolAddress ? PublicName : string.Empty;
            });
            int ordinaryLookups = 0;
            IDebugSymbols3 symbols = CreateSymbols(SymbolAddress + 0x35, 0x35, onLookup: () => ordinaryLookups++);
            DbgEngModuleSymbols moduleSymbols = new(advanced, symbols);

            HResult hr = moduleSymbols.GetSymbolByOffset(SymbolAddress + 0x35, out string name, out ulong displacement);

            Assert.Equal(HResult.S_OK, (int)hr);
            Assert.Equal(PublicName, name);
            Assert.Equal(0x35UL, displacement);
            Assert.Contains(SymbolAddress + 0x35, addresses);
            Assert.Contains(SymbolAddress, addresses);
            Assert.Equal(1, ordinaryLookups);
        }

        [Theory]
        [InlineData(HResult.S_FALSE)]
        [InlineData(HResult.E_FAIL)]
        public void OrdinaryLookupFailureDoesNotReturnAPartialResult(int result)
        {
            IDebugAdvanced2 advanced = CreateAdvanced(_ => PublicName);
            IDebugSymbols3 symbols = CreateSymbols(SymbolAddress + 1, 1, lookupResult: result);
            DbgEngModuleSymbols moduleSymbols = new(advanced, symbols);

            HResult hr = moduleSymbols.GetSymbolByOffset(SymbolAddress + 1, out string name, out ulong displacement);

            Assert.Equal(result, (int)hr);
            Assert.Null(name);
            Assert.Equal(0UL, displacement);
        }

        [Fact]
        public void PublicNameLookupFailureDoesNotPerformOrdinaryLookup()
        {
            IDebugAdvanced2 advanced = CreateProxy<IDebugAdvanced2>((_, arguments) => {
                arguments[5] = 0;
                arguments[8] = 0;
                return (int)HResult.E_FAIL;
            });
            IDebugSymbols3 symbols = CreateSymbols(SymbolAddress, 0,
                onLookup: () => throw new InvalidOperationException("Unexpected ordinary lookup."));
            DbgEngModuleSymbols moduleSymbols = new(advanced, symbols);

            HResult hr = moduleSymbols.GetSymbolByOffset(SymbolAddress, out string name, out ulong displacement);

            Assert.Equal(HResult.E_FAIL, (int)hr);
            Assert.Null(name);
            Assert.Equal(0UL, displacement);
        }

        [Fact]
        public void InvalidOrdinaryDisplacementDoesNotUnderflowSymbolBase()
        {
            IDebugAdvanced2 advanced = CreateAdvanced(_ => string.Empty);
            IDebugSymbols3 symbols = CreateSymbols(SymbolAddress, SymbolAddress + 1);
            DbgEngModuleSymbols moduleSymbols = new(advanced, symbols);

            HResult hr = moduleSymbols.GetSymbolByOffset(SymbolAddress, out string name, out ulong displacement);

            Assert.Equal(HResult.E_FAIL, (int)hr);
            Assert.Null(name);
            Assert.Equal(0UL, displacement);
        }

        [Fact]
        public void EmptyPublicNameAfterExportRetryDoesNotReturnAPartialResult()
        {
            IDebugAdvanced2 advanced = CreateAdvanced(_ => string.Empty);
            IDebugSymbols3 symbols = CreateSymbols(SymbolAddress + 0x35, 0x35);
            DbgEngModuleSymbols moduleSymbols = new(advanced, symbols);

            HResult hr = moduleSymbols.GetSymbolByOffset(SymbolAddress + 0x35, out string name, out ulong displacement);

            Assert.Equal(HResult.E_FAIL, (int)hr);
            Assert.Null(name);
            Assert.Equal(0UL, displacement);
        }

        private static IDebugAdvanced2 CreateAdvanced(Func<ulong, string> getName, int nullTerminators = 2)
        {
            return CreateProxy<IDebugAdvanced2>((method, arguments) => {
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
        }

        private static IDebugSymbols3 CreateSymbols(ulong expectedAddress, ulong displacement, int lookupResult = 0, Action onLookup = null)
        {
            return CreateProxy<IDebugSymbols3>((method, arguments) => {
                Assert.Equal(nameof(IDebugSymbols3.GetNameByOffsetWide), method.Name);
                Assert.Equal(expectedAddress, arguments[0]);
                Assert.Null(arguments[1]);
                Assert.Equal(0, arguments[2]);
                onLookup?.Invoke();
                arguments[3] = 1U;
                arguments[4] = displacement;
                return lookupResult;
            });
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
    }
}
