// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.FileFormats;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public class SpecialDiagInfoHeader : TStruct
    {
        public const string SPECIAL_DIAGINFO_SIGNATURE = "DIAGINFOHEADER";
        public const int SPECIAL_DIAGINFO_RUNTIME_BASEADDRESS = 2;
        public const int SPECIAL_DIAGINFO_LATEST = 2;

        public const ulong SpecialDiagInfoAddress_OSX = 0x7fffffff10000000UL;
        public const ulong SpecialDiagInfoAddress_64BIT = 0x00007ffffff10000UL;
        public const ulong SpecialDiagInfoAddress_32BIT = 0x000000007fff1000UL;
        public const int SpecialDiagInfoSize = 0x1000;

        [ArraySize(16)]
        public readonly byte[] RawSignature;
        public readonly int Version;
        public readonly ulong ExceptionRecordAddress;
        public readonly ulong RuntimeBaseAddress;       // Exists in version SPECIAL_DIAGINFO_RUNTIME_BASEADDRESS

        public static bool TryRead(IServiceProvider services, ulong address, out SpecialDiagInfoHeader info)
        {
            info = default;

            Reader reader = services.GetService<Reader>();
            if (reader is null)
            {
                return false;
            }

            try
            {
                info = reader.Read<SpecialDiagInfoHeader>(address);
            }
            catch (Exception ex) when (ex is InvalidVirtualAddressException or BadInputFormatException)
            {
                return false;
            }

            return true;
        }

        public static ulong GetAddress(IServiceProvider services)
        {
            ITarget target = services.GetService<ITarget>() ?? throw new DiagnosticsException("Dump or live session target required");
            IMemoryService memoryService = services.GetService<IMemoryService>();
            return target.OperatingSystem == OSPlatform.OSX ? SpecialDiagInfoAddress_OSX : (memoryService.PointerSize == 4 ? SpecialDiagInfoAddress_32BIT : SpecialDiagInfoAddress_64BIT);
        }

        public string Signature => Encoding.ASCII.GetString(RawSignature.Take(SPECIAL_DIAGINFO_SIGNATURE.Length).ToArray());

        public bool IsValid => Version > 0 && Signature == SPECIAL_DIAGINFO_SIGNATURE;
    }
}
