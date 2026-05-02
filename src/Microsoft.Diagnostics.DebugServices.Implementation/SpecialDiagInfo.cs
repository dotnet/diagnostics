// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ******************************************************************************
// WARNING!!!: This code is also used by createdump in the runtime repo.
// See: https://github.com/dotnet/runtime/blob/main/src/coreclr/debug/createdump/specialdiaginfo.h
// ******************************************************************************

using System;
using System.Collections.Generic;
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

        // Apple Silicon (arm64) macOS user-space VM is 47 bits; addresses above 0x7FFF_FFFF_FFFF
        // are not mappable and lldb's core file reader rejects segments at those addresses. Newer
        // createdump targets a 47-bit-valid address there. The legacy x86_64 macOS address is kept
        // as a fallback so dumps produced by older createdump binaries are still recognized.
        private const ulong SpecialDiagInfoAddressMacOSArm64 = 0x00007ffffff10000;
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
            _target = target ?? throw new DiagnosticsException("Dump or live session target required");
            _memoryService = memoryService;
        }

        private IEnumerable<ulong> SpecialDiagInfoAddresses
        {
            get
            {
                if (_target.OperatingSystem == OSPlatform.OSX)
                {
                    if (_memoryService.PointerSize == 8)
                    {
                        // Try the arm64-valid address first (also valid on x86_64 macOS for newer
                        // createdump output); fall back to the legacy x86_64 address.
                        yield return SpecialDiagInfoAddressMacOSArm64;
                        if (SpecialDiagInfoAddressMacOSArm64 != SpecialDiagInfoAddressMacOS64)
                        {
                            yield return SpecialDiagInfoAddressMacOS64;
                        }
                    }
                }
                else if (_target.OperatingSystem == OSPlatform.Linux)
                {
                    if (_memoryService.PointerSize == 8)
                    {
                        yield return SpecialDiagInfoAddress64;
                    }
                    else
                    {
                        yield return SpecialDiagInfoAddress32;
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if the special diagnostic info header is present in the dump. This indicates the dump
        /// was collected by createdump (or the .NET runtime's crash handler) rather than a system dump tool.
        /// </summary>
        public bool HasDiagnosticInfo()
        {
            Span<byte> headerBuffer = stackalloc byte[Unsafe.SizeOf<SpecialDiagInfoHeader>()];
            foreach (ulong address in SpecialDiagInfoAddresses)
            {
                if (_memoryService.ReadMemory(address, headerBuffer, out int bytesRead) && bytesRead == headerBuffer.Length)
                {
                    SpecialDiagInfoHeader header = Unsafe.As<byte, SpecialDiagInfoHeader>(ref MemoryMarshal.GetReference(headerBuffer));
                    ReadOnlySpan<byte> signature = new(header.Signature, SPECIAL_DIAGINFO_SIGNATURE.Length);
                    if (signature.SequenceEqual(SPECIAL_DIAGINFO_SIGNATURE))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Checks whether the dump was created by createdump and emits a warning if not.
        /// Only applicable for Linux/macOS dumps.
        /// </summary>
        public static void WarnIfNotCreatedump(ITarget target, IConsoleService console)
        {
            if (!target.IsDump || target.OperatingSystem == OSPlatform.Windows)
            {
                return;
            }

            try
            {
                IMemoryService memoryService = target.Services.GetService<IMemoryService>();
                if (memoryService == null)
                {
                    return;
                }

                SpecialDiagInfo diagInfo = new(target, memoryService);
                if (!diagInfo.HasDiagnosticInfo())
                {
                    console.WriteWarning(
                        "WARNING: This dump doesn't contain memory the .NET runtime's dump functionality usually adds. " +
                        "System dumps may be missing memory regions required by SOS commands. " +
                        "For best results, collect dumps using the appropriate tool for your scenario:  https://aka.ms/dotnet-dump-collection" + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"SpecialDiagInfo: Failed to check for diagnostic info header: {ex.Message}");
            }
        }

        public static ICrashInfoService CreateCrashInfoServiceFromException(IServiceProvider services)
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
            Span<byte> exceptionRecordBuffer = stackalloc byte[Unsafe.SizeOf<EXCEPTION_RECORD64>()];
            foreach (ulong address in SpecialDiagInfoAddresses)
            {
                if (_memoryService.ReadMemory(address, headerBuffer, out int bytesRead) && bytesRead == headerBuffer.Length)
                {
                    SpecialDiagInfoHeader header = Unsafe.As<byte, SpecialDiagInfoHeader>(ref MemoryMarshal.GetReference(headerBuffer));
                    ReadOnlySpan<byte> signature = new(header.Signature, SPECIAL_DIAGINFO_SIGNATURE.Length);
                    if (signature.SequenceEqual(SPECIAL_DIAGINFO_SIGNATURE))
                    {
                        if (header.Version >= SPECIAL_DIAGINFO_VERSION && header.ExceptionRecordAddress != 0)
                        {
                            if (_memoryService.ReadMemory(header.ExceptionRecordAddress, exceptionRecordBuffer, out bytesRead) && bytesRead == exceptionRecordBuffer.Length)
                            {
                                return Unsafe.As<byte, EXCEPTION_RECORD64>(ref MemoryMarshal.GetReference(exceptionRecordBuffer));
                            }
                        }
                        return default;
                    }
                }
            }
            return default;
        }
    }
}
