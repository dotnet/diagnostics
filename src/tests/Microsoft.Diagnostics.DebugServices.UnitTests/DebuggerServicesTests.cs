// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
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
            DebuggerServices.IDebugAdvancedOperations advanced = CreateAdvanced(_ => PublicName, nullTerminators);
            DebuggerServices.IDebugSymbolsOperations symbols = CreateSymbols(SymbolAddress + offset, offset);
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
            DebuggerServices.IDebugAdvancedOperations advanced = CreateAdvanced(address => {
                addresses.Add(address);
                return address == SymbolAddress ? PublicName : string.Empty;
            });
            int ordinaryLookups = 0;
            DebuggerServices.IDebugSymbolsOperations symbols = CreateSymbols(
                SymbolAddress + 0x35,
                0x35,
                onLookup: () => ordinaryLookups++);
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
            DebuggerServices.IDebugAdvancedOperations advanced = CreateAdvanced(_ => PublicName);
            DebuggerServices.IDebugSymbolsOperations symbols = CreateSymbols(SymbolAddress + 1, 1, lookupResult: result);
            DbgEngModuleSymbols moduleSymbols = new(advanced, symbols);

            HResult hr = moduleSymbols.GetSymbolByOffset(SymbolAddress + 1, out string name, out ulong displacement);

            Assert.Equal(result, (int)hr);
            Assert.Null(name);
            Assert.Equal(0UL, displacement);
        }

        [Fact]
        public void PublicNameLookupFailureDoesNotPerformOrdinaryLookup()
        {
            IDebugAdvanced2 advancedProxy = CreateProxy<IDebugAdvanced2>((_, arguments) => {
                arguments[5] = 0;
                arguments[8] = 0;
                return (int)HResult.E_FAIL;
            });
            DebuggerServices.IDebugAdvancedOperations advanced = new DebugAdvancedOperations(advancedProxy);
            DebuggerServices.IDebugSymbolsOperations symbols = CreateSymbols(SymbolAddress, 0,
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
            DebuggerServices.IDebugAdvancedOperations advanced = CreateAdvanced(_ => string.Empty);
            DebuggerServices.IDebugSymbolsOperations symbols = CreateSymbols(SymbolAddress, SymbolAddress + 1);
            DbgEngModuleSymbols moduleSymbols = new(advanced, symbols);

            HResult hr = moduleSymbols.GetSymbolByOffset(SymbolAddress, out string name, out ulong displacement);

            Assert.Equal(HResult.E_FAIL, (int)hr);
            Assert.Null(name);
            Assert.Equal(0UL, displacement);
        }

        [Fact]
        public void EmptyPublicNameAfterExportRetryDoesNotReturnAPartialResult()
        {
            DebuggerServices.IDebugAdvancedOperations advanced = CreateAdvanced(_ => string.Empty);
            DebuggerServices.IDebugSymbolsOperations symbols = CreateSymbols(SymbolAddress + 0x35, 0x35);
            DbgEngModuleSymbols moduleSymbols = new(advanced, symbols);

            HResult hr = moduleSymbols.GetSymbolByOffset(SymbolAddress + 0x35, out string name, out ulong displacement);

            Assert.Equal(HResult.E_FAIL, (int)hr);
            Assert.Null(name);
            Assert.Equal(0UL, displacement);
        }

        [Fact]
        public void DebugAdvancedWrapperUsesNativeVTableAndBalancesReferences()
        {
            TestDebugAdvanced advanced = new();
            int finalReferenceCount = -1;
            try
            {
                Assert.Equal(1, advanced.ReferenceCount);
                // CallableCOMWrapper consumes the incoming reference after querying the requested interface.
                Assert.Equal(2, advanced.AddRef());
                using (DebugAdvancedWrapper wrapper = new(advanced.Interface))
                {
                    Assert.Equal(2, advanced.ReferenceCount);

                    HResult hr = wrapper.GetSymbolInformation(
                        DEBUG_SYMINFO.GET_SYMBOL_NAME_BY_OFFSET_AND_TAG_WIDE,
                        SymbolAddress,
                        10,
                        null,
                        out int infoSize);
                    Assert.Equal(HResult.S_FALSE, (int)hr);
                    Assert.Equal(advanced.SymbolBytes.Length, infoSize);

                    byte[] buffer = new byte[infoSize];
                    hr = wrapper.GetSymbolInformation(
                        DEBUG_SYMINFO.GET_SYMBOL_NAME_BY_OFFSET_AND_TAG_WIDE,
                        SymbolAddress,
                        10,
                        buffer,
                        out infoSize);
                    Assert.Equal(HResult.S_OK, (int)hr);
                    Assert.Equal(advanced.SymbolBytes.Length, infoSize);
                    Assert.Equal(advanced.SymbolBytes, buffer);
                    Assert.Equal(new[] { 0, buffer.Length }, advanced.BufferSizes);
                    Assert.Equal(DEBUG_SYMINFO.GET_SYMBOL_NAME_BY_OFFSET_AND_TAG_WIDE, advanced.Which);
                    Assert.Equal(SymbolAddress, advanced.Arg64);
                    Assert.Equal(10U, advanced.Arg32);
                    Assert.True(advanced.StringBufferWasNull);
                }
                Assert.Equal(1, advanced.ReferenceCount);
            }
            finally
            {
                finalReferenceCount = advanced.Release();
            }
            Assert.Equal(0, finalReferenceCount);
            Assert.True(advanced.Destroyed);
        }

        [Fact]
        public void DebugSymbolsWrapperPreserves64BitDisplacementAndBalancesReferences()
        {
            TestDebugSymbols symbols = new();
            int finalReferenceCount = -1;
            try
            {
                Assert.Equal(1, symbols.ReferenceCount);
                // CallableCOMWrapper consumes the incoming reference after querying the requested interface.
                Assert.Equal(2, symbols.AddRef());
                using (DebugSymbolsWrapper wrapper = new(symbols.Interface))
                {
                    Assert.Equal(2, symbols.ReferenceCount);

                    HResult hr = wrapper.GetNameByOffsetWide(SymbolAddress, out ulong displacement);

                    Assert.Equal(HResult.S_OK, (int)hr);
                    Assert.Equal(TestDebugSymbols.Displacement, displacement);
                    Assert.Equal(SymbolAddress, symbols.Offset);
                    Assert.True(symbols.NameBufferWasNull);
                    Assert.Equal(0, symbols.NameBufferSize);
                    Assert.True(symbols.NameSizeWasNull);
                }
                Assert.Equal(1, symbols.ReferenceCount);
            }
            finally
            {
                finalReferenceCount = symbols.Release();
            }
            Assert.Equal(0, finalReferenceCount);
            Assert.True(symbols.Destroyed);
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

        private static DebuggerServices.IDebugSymbolsOperations CreateSymbols(
            ulong expectedAddress,
            ulong displacement,
            int lookupResult = 0,
            Action onLookup = null)
        {
            IDebugSymbols3 proxy = CreateProxy<IDebugSymbols3>((method, arguments) => {
                Assert.Equal(nameof(IDebugSymbols3.GetNameByOffsetWide), method.Name);
                Assert.Equal(expectedAddress, arguments[0]);
                Assert.Null(arguments[1]);
                Assert.Equal(0, arguments[2]);
                onLookup?.Invoke();
                arguments[3] = 1U;
                arguments[4] = displacement;
                return lookupResult;
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
        }

        private abstract class TestCOMObject : COMCallableIUnknown
        {
            public int ReferenceCount
            {
                get
                {
                    int count = AddRef();
                    Release();
                    return count - 1;
                }
            }

            public bool Destroyed { get; private set; }

            protected override void Destroy() => Destroyed = true;

            [UnmanagedFunctionPointer(CallingConvention.Winapi)]
            protected delegate int UnusedDelegate(IntPtr self);

            protected static int Unused(IntPtr self) => HResult.E_NOTIMPL;
        }

        private sealed class TestDebugAdvanced : TestCOMObject
        {
            public TestDebugAdvanced()
            {
                VTableBuilder builder = AddInterface(typeof(IDebugAdvanced2).GUID, validate: false);
                for (int i = 0; i < 5; i++)
                {
                    builder.AddMethod(new UnusedDelegate(Unused));
                }
                builder.AddMethod(new GetSymbolInformationDelegate(GetSymbolInformation));
                Interface = builder.Complete();
                AddRef();
            }

            public IntPtr Interface { get; }
            public byte[] SymbolBytes { get; } = Encoding.Unicode.GetBytes(PublicName + '\0');
            public List<int> BufferSizes { get; } = new();
            public DEBUG_SYMINFO Which { get; private set; }
            public ulong Arg64 { get; private set; }
            public uint Arg32 { get; private set; }
            public bool StringBufferWasNull { get; private set; }

            private int GetSymbolInformation(
                IntPtr self,
                DEBUG_SYMINFO which,
                ulong arg64,
                uint arg32,
                IntPtr buffer,
                int bufferSize,
                IntPtr infoSize,
                IntPtr stringBuffer,
                int stringBufferSize,
                IntPtr stringSize)
            {
                Which = which;
                Arg64 = arg64;
                Arg32 = arg32;
                BufferSizes.Add(bufferSize);
                StringBufferWasNull = stringBuffer == IntPtr.Zero && stringBufferSize == 0 && stringSize == IntPtr.Zero;
                Marshal.WriteInt32(infoSize, SymbolBytes.Length);
                if (buffer == IntPtr.Zero)
                {
                    return HResult.S_FALSE;
                }
                Marshal.Copy(SymbolBytes, 0, buffer, SymbolBytes.Length);
                return HResult.S_OK;
            }

            [UnmanagedFunctionPointer(CallingConvention.Winapi)]
            private delegate int GetSymbolInformationDelegate(
                IntPtr self,
                DEBUG_SYMINFO which,
                ulong arg64,
                uint arg32,
                IntPtr buffer,
                int bufferSize,
                IntPtr infoSize,
                IntPtr stringBuffer,
                int stringBufferSize,
                IntPtr stringSize);
        }

        private sealed class TestDebugSymbols : TestCOMObject
        {
            public const ulong Displacement = 0x1_0000_0035;

            public TestDebugSymbols()
            {
                VTableBuilder builder = AddInterface(typeof(IDebugSymbols5).GUID, validate: false);
                for (int i = 0; i < 57; i++)
                {
                    builder.AddMethod(new UnusedDelegate(Unused));
                }
                builder.AddMethod(new GetNameByOffsetWideDelegate(GetNameByOffsetWide));
                Interface = builder.Complete();
                AddRef();
            }

            public IntPtr Interface { get; }
            public ulong Offset { get; private set; }
            public bool NameBufferWasNull { get; private set; }
            public int NameBufferSize { get; private set; }
            public bool NameSizeWasNull { get; private set; }

            private int GetNameByOffsetWide(
                IntPtr self,
                ulong offset,
                IntPtr nameBuffer,
                int nameBufferSize,
                IntPtr nameSize,
                IntPtr displacement)
            {
                Offset = offset;
                NameBufferWasNull = nameBuffer == IntPtr.Zero;
                NameBufferSize = nameBufferSize;
                NameSizeWasNull = nameSize == IntPtr.Zero;
                Marshal.WriteInt64(displacement, unchecked((long)Displacement));
                return HResult.S_OK;
            }

            [UnmanagedFunctionPointer(CallingConvention.Winapi)]
            private delegate int GetNameByOffsetWideDelegate(
                IntPtr self,
                ulong offset,
                IntPtr nameBuffer,
                int nameBufferSize,
                IntPtr nameSize,
                IntPtr displacement);
        }
    }
}
