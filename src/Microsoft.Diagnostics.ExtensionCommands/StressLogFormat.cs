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
        private static readonly StressLogFacility[] s_facilityFlags =
        {
            StressLogFacility.GC,
            StressLogFacility.GCRoots,
            StressLogFacility.GCAlloc,
            StressLogFacility.GCInfo,
            StressLogFacility.EEMem,
            StressLogFacility.Always,
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
            StringBuilder sb = new();
            sb.Append('`');

            uint known = 0;
            foreach (StressLogFacility flag in s_facilityFlags)
            {
                known |= (uint)flag;
                if ((facility & flag) != 0)
                {
                    sb.Append(flag).Append('`');
                }
            }

            uint leftover = (uint)facility & ~known;
            if (leftover != 0)
            {
                sb.Append("0x").Append(leftover.ToString("x")).Append('`');
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
