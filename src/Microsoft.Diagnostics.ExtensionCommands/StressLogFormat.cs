// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.StressLogs;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    /// <summary>
    /// Shared rendering for the managed stress log commands: the "STRESS LOG:"
    /// header (used by both !dumplog and !histinit) and the per-message text
    /// formatter that resolves typed pointers to runtime names.
    /// </summary>
    internal static class StressLogFormat
    {
        // Runtime facility names indexed by bit position (0..31), mirroring the native
        // getFacilityName walk over coreclr/inc/loglf.h. ClrMD's StressLogFacility enum
        // cannot be used for this: its named values (e.g. GCRoots = 0x2) do NOT match the
        // runtime's actual facility bits (LF_GCROOTS = 0x80000; 0x2 is LF_GCINFO), so the
        // raw facility value must be decoded against the runtime's own bit table.
        private static readonly string[] s_facilityNames =
        {
            "GC",                // 0x00000001
            "GCINFO",            // 0x00000002
            "STUBS",             // 0x00000004
            "JIT",               // 0x00000008
            "LOADER",            // 0x00000010
            "METADATA",          // 0x00000020
            "SYNC",              // 0x00000040
            "EEMEM",             // 0x00000080
            "GCALLOC",           // 0x00000100
            "CORDB",             // 0x00000200
            "CLASSLOADER",       // 0x00000400
            "CORPROF",           // 0x00000800
            "DIAGNOSTICS_PORT",  // 0x00001000
            "DBGALLOC",          // 0x00002000
            "EH",                // 0x00004000
            "ENC",               // 0x00008000
            "ASSERT",            // 0x00010000
            "VERIFIER",          // 0x00020000
            "THREADPOOL",        // 0x00040000
            "GCROOTS",           // 0x00080000
            "INTEROP",           // 0x00100000
            "MARSHALER",         // 0x00200000
            "TIEREDCOMPILATION", // 0x00400000
            "ZAP",               // 0x00800000
            "STARTUP",           // 0x01000000
            "APPDOMAIN",         // 0x02000000
            "CODESHARING",       // 0x04000000
            "STORE",             // 0x08000000
            "SECURITY",          // 0x10000000
            "LOCKS",             // 0x20000000
            "BCL",               // 0x40000000
            "ALWAYS",            // 0x80000000
        };

        public static void WriteHeader(TextWriter writer, StressLog log, int threadCount, double elapsedSeconds)
            => WriteHeader(writer, log.FacilitiesToLog, log.LevelToLog, log.MaxSizePerThread, log.MaxSizeTotal,
                           log.ChunkCount, log.TickFrequency, log.StartTimeUtc, threadCount, elapsedSeconds);

        public static void WriteHeader(TextWriter writer, GCHistory history)
            => WriteHeader(writer, history.FacilitiesToLog, history.LevelToLog, history.MaxSizePerThread, history.MaxSizeTotal,
                           history.ChunkCount, history.TickFrequency, history.StartTimeUtc, history.ThreadCount, history.ElapsedSeconds);

        private static void WriteHeader(TextWriter writer, uint? facilitiesToLog, uint? levelToLog, uint? maxSizePerThread,
                                        uint? maxSizeTotal, int? chunkCount, ulong tickFrequency, DateTime? startTimeUtc,
                                        int threadCount, double elapsedSeconds)
        {
            writer.WriteLine("STRESS LOG:");
            writer.WriteLine($"    facilitiesToLog  = {Hex(facilitiesToLog)}");
            writer.WriteLine($"    levelToLog       = {Dec(levelToLog)}");
            writer.WriteLine($"    MaxLogSizePerThread = {HexWithDec(maxSizePerThread)}");
            writer.WriteLine($"    MaxTotalLogSize = {HexWithDec(maxSizeTotal)}");
            writer.WriteLine($"    CurrentTotalLogChunk = {Dec(chunkCount)}");
            writer.WriteLine($"    ThreadsWithLogs  = {threadCount}");
            writer.WriteLine($"    Clock frequency  = {tickFrequency / 1.0E9:0.000} GHz");
            writer.WriteLine($"    Start time         {Time(startTimeUtc)}");
            writer.WriteLine($"    Last message time  {Time(LastMessageTime(startTimeUtc, elapsedSeconds))}");
            writer.WriteLine($"    Total elapsed time {elapsedSeconds:0.000} sec");
        }

        /// <summary>
        /// Renders the message text of a stress log message, resolving MethodTable
        /// and MethodDesc pointers to runtime type/method names.
        /// </summary>
        public static string FormatMessageText(ClrRuntime runtime, StressLogMessage message, int pointerHexDigits)
        {
            StringBuilder sb = new();
            StressLogMessageWriter receiver = new(runtime, pointerHexDigits, sb);
            message.Format(ref receiver);
            return sb.ToString();
        }

        /// <summary>Renders a facility bitmask as a backtick-delimited list of names.</summary>
        public static string FacilityName(StressLogFacility facility)
        {
            uint value = (uint)facility;

            // Mirrors the native getFacilityName: LF_ALL renders as a single name.
            if (value == 0xFFFFFFFF)
            {
                return "`ALL`";
            }

            StringBuilder sb = new();
            sb.Append('`');

            for (int bit = 0; bit < s_facilityNames.Length; bit++)
            {
                if ((value & (1u << bit)) != 0)
                {
                    sb.Append(s_facilityNames[bit]).Append('`');
                }
            }

            return sb.ToString();
        }

        private static DateTime? LastMessageTime(DateTime? startTimeUtc, double elapsedSeconds)
            => startTimeUtc.HasValue ? startTimeUtc.Value.AddSeconds(elapsedSeconds) : (DateTime?)null;

        private static string Hex(uint? value) => value.HasValue ? $"0x{value.Value:x}" : "n/a";

        private static string Dec(uint? value) => value.HasValue ? value.Value.ToString() : "n/a";

        private static string Dec(int? value) => value.HasValue ? value.Value.ToString() : "n/a";

        private static string HexWithDec(uint? value) => value.HasValue ? $"0x{value.Value:x} ({value.Value})" : "n/a";

        private static string Time(DateTime? utc) => utc.HasValue ? utc.Value.ToLocalTime().ToString("HH:mm:ss") : "n/a";
    }

    /// <summary>
    /// An <see cref="IStressLogFormatReceiver"/> that renders a stress log message
    /// into a <see cref="StringBuilder"/>, resolving MethodTable/MethodDesc pointers
    /// to runtime names. Mirrors the native formatOutput special-pointer handling.
    /// </summary>
    internal struct StressLogMessageWriter : IStressLogFormatReceiver
    {
        private readonly ClrRuntime _runtime;
        private readonly int _pointerHexDigits;
        private readonly StringBuilder _sb;

        public StressLogMessageWriter(ClrRuntime runtime, int pointerHexDigits, StringBuilder sb)
        {
            _runtime = runtime;
            _pointerHexDigits = pointerHexDigits;
            _sb = sb;
        }

        public void Literal(ReadOnlySpan<byte> ascii)
        {
            foreach (byte b in ascii)
            {
                _sb.Append((char)b);
            }
        }

        public void Integer(StressLogIntegerKind kind, int width, int precision, long value)
        {
            string formatted = kind switch
            {
                StressLogIntegerKind.Hex => ((ulong)value).ToString("x"),
                StressLogIntegerKind.HexUpper => ((ulong)value).ToString("X"),
                StressLogIntegerKind.Unsigned => ((ulong)value).ToString(),
                StressLogIntegerKind.Char => ((char)value).ToString(),
                _ => value.ToString(),
            };

            if (precision > 0 && kind != StressLogIntegerKind.Char && formatted.Length < precision)
            {
                formatted = formatted.PadLeft(precision, '0');
            }

            if (width > 0 && formatted.Length < width)
            {
                formatted = formatted.PadLeft(width);
            }

            _sb.Append(formatted);
        }

        public void Pointer(StressLogPointerKind kind, ulong address)
        {
            _sb.Append(address.ToString("x" + _pointerHexDigits.ToString()));

            switch (kind)
            {
                case StressLogPointerKind.MethodTable:
                    ulong methodTable = address & ~3ul;
                    if ((address & 3) != 0)
                    {
                        _sb.Append(" Low Bit(s) Set");
                    }

                    ClrType type = _runtime.GetTypeByMethodTable(methodTable);
                    _sb.Append(type is null ? " (BAD MethodTable)" : $" ({type.Name})");
                    break;

                case StressLogPointerKind.MethodDesc:
                    ClrMethod method = _runtime.GetMethodByHandle(address);
                    _sb.Append(method is null ? " (BAD Method)" : $" ({method.Signature})");
                    break;

                default:
                    break;
            }
        }

        public void String(StressLogStringEncoding encoding, ReadOnlySpan<byte> sanitized)
        {
            foreach (byte b in sanitized)
            {
                _sb.Append((char)b);
            }
        }

        public void MissingArgument()
        {
            _sb.Append("<missing>");
        }
    }
}
