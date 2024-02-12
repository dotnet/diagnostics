// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ******************************************************************************
// WARNING!!!: This code is also used by createdump in the runtime repo.
// See: https://github.com/dotnet/runtime/blob/main/src/coreclr/debug/createdump/specialdiaginfo.h
// ******************************************************************************

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// This is a special memory region added to ELF and MachO dumps that contains extra diagnostics
    /// information like the exception record for a crash for a NativeAOT app. The exception record
    /// contains the pointer to the JSON formatted crash info.
    /// </summary>
    public unsafe class SpecialDiagInfo
    {
        private static readonly byte[] SPECIAL_DIAGINFO_SIGNATURE = Encoding.ASCII.GetBytes("DIAGINFOHEADER");
        private const int SPECIAL_DIAGINFO_VERSION = 1;

        private const ulong SpecialDiagInfoAddressMacOS64 = 0x7fffffff10000000;
        private const ulong SpecialDiagInfoAddress64 = 0x00007ffffff10000;
        private const ulong SpecialDiagInfoAddress32 = 0x7fff1000;

        [StructLayout(LayoutKind.Sequential)]
        private struct SpecialDiagInfoHeader
        {
            public const int SignatureSize = 16;
            public fixed byte Signature[SignatureSize];
            public int Version;
            public ulong ExceptionRecordAddress;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EXCEPTION_RECORD64
        {
            public uint ExceptionCode;
            public uint ExceptionFlags;
            public ulong ExceptionRecord;
            public ulong ExceptionAddress;
            public uint NumberParameters;
            public uint __unusedAlignment;
            public fixed ulong ExceptionInformation[15]; //EXCEPTION_MAXIMUM_PARAMETERS
        }

        private readonly ITarget _target;
        private readonly IMemoryService _memoryService;

        public SpecialDiagInfo(ITarget target, IMemoryService memoryService)
        {
            _target = target;
            _memoryService = memoryService;
        }

        private ulong SpecialDiagInfoAddress
        {
            get
            {
                if (_target.OperatingSystem == OSPlatform.OSX)
                {
                    if (_memoryService.PointerSize == 8)
                    {
                        return SpecialDiagInfoAddressMacOS64;
                    }
                }
                else if (_target.OperatingSystem == OSPlatform.Linux)
                {
                    if (_memoryService.PointerSize == 8)
                    {
                        return SpecialDiagInfoAddress64;
                    }
                    else
                    {
                        return SpecialDiagInfoAddress32;
                    }
                }
                return 0;
            }
        }

        public static ICrashInfoService CreateCrashInfoService(IServiceProvider services)
        {
            EXCEPTION_RECORD64 exceptionRecord;

            SpecialDiagInfo diagInfo = new(services.GetService<ITarget>(), services.GetService<IMemoryService>());
            exceptionRecord = diagInfo.GetExceptionRecord();

            if (exceptionRecord.ExceptionCode == CrashInfoService.STATUS_STACK_BUFFER_OVERRUN &&
                exceptionRecord.NumberParameters >= 4 &&
                exceptionRecord.ExceptionInformation[0] == CrashInfoService.FAST_FAIL_EXCEPTION_DOTNET_AOT)
            {
                uint hresult = (uint)exceptionRecord.ExceptionInformation[1];
                ulong triageBufferAddress = exceptionRecord.ExceptionInformation[2];
                int triageBufferSize = (int)exceptionRecord.ExceptionInformation[3];

                Span<byte> buffer = new byte[triageBufferSize];
                if (services.GetService<IMemoryService>().ReadMemory(triageBufferAddress, buffer, out int bytesRead) && bytesRead == triageBufferSize)
                {
                    return CrashInfoService.Create(hresult, buffer, services.GetService<IModuleService>());
                }
                else
                {
                    Trace.TraceError($"SpecialDiagInfo: ReadMemory({triageBufferAddress}) failed");
                }
            }
            return null;
        }

        internal EXCEPTION_RECORD64 GetExceptionRecord()
        {
            Span<byte> headerBuffer = stackalloc byte[Unsafe.SizeOf<SpecialDiagInfoHeader>()];
            if (_memoryService.ReadMemory(SpecialDiagInfoAddress, headerBuffer, out int bytesRead) && bytesRead == headerBuffer.Length)
            {
                SpecialDiagInfoHeader header = Unsafe.As<byte, SpecialDiagInfoHeader>(ref MemoryMarshal.GetReference(headerBuffer));
                ReadOnlySpan<byte> signature = new(header.Signature, SPECIAL_DIAGINFO_SIGNATURE.Length);
                if (signature.SequenceEqual(SPECIAL_DIAGINFO_SIGNATURE))
                {
                    if (header.Version >= SPECIAL_DIAGINFO_VERSION && header.ExceptionRecordAddress != 0)
                    {
                        Span<byte> exceptionRecordBuffer = stackalloc byte[Unsafe.SizeOf<EXCEPTION_RECORD64>()];
                        if (_memoryService.ReadMemory(header.ExceptionRecordAddress, exceptionRecordBuffer, out bytesRead) && bytesRead == exceptionRecordBuffer.Length)
                        {
                            return Unsafe.As<byte, EXCEPTION_RECORD64>(ref MemoryMarshal.GetReference(exceptionRecordBuffer));
                        }
                    }
                }
            }
            return default;
        }
    }
}
