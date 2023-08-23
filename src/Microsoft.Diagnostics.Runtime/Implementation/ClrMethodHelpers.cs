// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Microsoft.Diagnostics.Runtime.DacInterface;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class ClrMethodHelpers : IClrMethodHelpers
    {
        private readonly ClrDataProcess _clrDataProcess;
        private readonly SOSDac _sos;
        private readonly CacheOptions _cacheOptions;

        public IDataReader DataReader { get; }

        public ClrMethodHelpers(ClrDataProcess clrDataProcess, SOSDac sos, IDataReader reader, CacheOptions cacheOptions)
        {
            _clrDataProcess = clrDataProcess;
            _sos = sos;
            _cacheOptions = cacheOptions;
            DataReader = reader;
        }

        public bool GetSignature(ulong methodDesc, out string? signature)
        {
            signature = _sos.GetMethodDescName(methodDesc);

            // Always cache an empty name, no reason to keep requesting it.
            // Implementations may ignore this (ClrmdMethod doesn't cache null signatures).
            if (string.IsNullOrWhiteSpace(signature))
                return true;

            if (_cacheOptions.CacheMethodNames == StringCaching.Intern)
                signature = string.Intern(signature);

            return _cacheOptions.CacheMethodNames != StringCaching.None;
        }

        public ulong GetILForModule(ulong address, uint rva) => _sos.GetILForModule(address, rva);

        public ImmutableArray<ILToNativeMap> GetILMap(ClrMethod inMethod)
        {
            ImmutableArray<ILToNativeMap>.Builder result = ImmutableArray.CreateBuilder<ILToNativeMap>();

            foreach (ClrDataMethod method in _clrDataProcess.EnumerateMethodInstancesByAddress(inMethod.NativeCode))
            {
                ILToNativeMap[]? map = method.GetILToNativeMap();
                if (map != null)
                {
                    for (int i = 0; i < map.Length; i++)
                    {
                        if (map[i].StartAddress > map[i].EndAddress)
                        {
                            if (i + 1 == map.Length)
                                map[i].EndAddress = FindEnd(inMethod.HotColdInfo, map[i].StartAddress);
                            else
                                map[i].EndAddress = map[i + 1].StartAddress - 1;
                        }
                    }

                    result.AddRange(map);
                }

                method.Dispose();
            }

            return result.MoveOrCopyToImmutable();
        }

        private static ulong FindEnd(HotColdRegions reg, ulong address)
        {
            ulong hotEnd = reg.HotStart + reg.HotSize;
            if (reg.HotStart <= address && address < hotEnd)
                return hotEnd;

            ulong coldEnd = reg.ColdStart + reg.ColdSize;
            if (reg.ColdStart <= address && address < coldEnd)
                return coldEnd;

            // Shouldn't reach here, but give a sensible answer if we do.
            return address + 0x20;
        }
    }
}
