﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal static class Formats
    {
        private static HexValueFormat s_hexOffsetFormat;
        private static HexValueFormat s_hexValueFormat;
        private static Format s_text;
        private static IntegerFormat s_integerFormat;
        private static TypeNameFormat s_typeNameFormat;

        static Formats()
        {
            int pointerSize = IntPtr.Size;
            Pointer = new IntegerFormat(pointerSize == 4 ? "x8" : "x12");
        }

        public static Format Pointer { get; }

        public static Format HexOffset => s_hexOffsetFormat ??= new(printPrefix: true, signed: true);
        public static Format HexValue => s_hexValueFormat ??= new(printPrefix: true, signed: false);
        public static Format IntegerWithCommas => s_integerFormat ??= new("n0");
        public static Format Text => s_text ??= new(true);
        public static Format TypeName => s_typeNameFormat ??= new();

        private sealed class IntegerFormat : Format
        {
            private readonly string _format;

            public IntegerFormat(string format)
            {
                _format = "{0:" + format + "}";
            }

            public override int FormatValue(StringBuilder result, object value, int maxLength, bool truncateBegin)
            {
                value = Unwrap(value);

                int startLength = result.Length;
                switch (value)
                {
                    case null:
                        break;

                    default:
                        result.AppendFormat(_format, value);
                        break;
                }

                TruncateStringBuilder(result, maxLength, result.Length - startLength, truncateBegin);
                return result.Length - startLength;
            }
        }

        private sealed class TypeNameFormat : Format
        {
            private const string UnknownTypeName = "Unknown";

            public override int FormatValue(StringBuilder sb, object value, int maxLength, bool truncateBegin)
            {
                int startLength = sb.Length;

                if (value is null)
                {
                    sb.Append(UnknownTypeName);
                }
                else if (value is ClrType type)
                {
                    string typeName = type.Name;
                    if (!string.IsNullOrWhiteSpace(typeName))
                    {
                        sb.Append(typeName);
                    }
                    else
                    {
                        string module = type.Module?.Name;
                        if (!string.IsNullOrWhiteSpace(module))
                        {
                            try
                            {
                                module = System.IO.Path.GetFileNameWithoutExtension(module);
                                sb.Append(module);
                                sb.Append('!');
                            }
                            catch (ArgumentException)
                            {
                            }
                        }

                        sb.Append(UnknownTypeName);
                        if (type.MethodTable != 0)
                        {
                            sb.Append($" (MethodTable: ");
                            sb.AppendFormat("{0:x12}", type.MethodTable);
                            sb.Append(')');
                        }
                    }
                }
                else
                {
                    sb.Append(value);
                }

                TruncateStringBuilder(sb, maxLength, sb.Length - startLength, truncateBegin);
                return sb.Length - startLength;
            }
        }

        private sealed class HexValueFormat : Format
        {
            public bool PrintPrefix { get; }
            public bool Signed { get; }

            public HexValueFormat(bool printPrefix, bool signed)
            {
                PrintPrefix = printPrefix;
                Signed = signed;
            }

            private string GetStringValue(long offset)
            {
                if (Signed)
                {
                    if (PrintPrefix)
                    {
                        return offset < 0 ? $"-0x{Math.Abs(offset):x2}" : $"0x{offset:x2}";
                    }
                    else
                    {
                        return offset < 0 ? $"-{Math.Abs(offset):x2}" : $"{offset:x2}";
                    }
                }

                return PrintPrefix ? $"0x{offset:x2}" : offset.ToString("x2");
            }

            private string GetHexOffsetString(object value)
            {
                return value switch
                {
                    null => "",
                    nint ni => GetStringValue(ni),
                    nuint nui => PrintPrefix ? $"0x{nui:x2}" : "{nui:x2}",
                    ulong ul => PrintPrefix ? $"0x{ul:x2}" : ul.ToString("x2"),
                    long l => GetStringValue(l),
                    int i => GetStringValue(i),
                    uint u => PrintPrefix ? $"0x{u:x2}" : u.ToString("x2"),
                    IEnumerable<byte> bytes => (PrintPrefix ? "0x" : "") + string.Join("", bytes.Select(b => b.ToString("x2"))),
                    _ => throw new InvalidOperationException($"Cannot convert value of type {value.GetType().FullName} to a HexOffset")
                };
            }

            public override int FormatValue(StringBuilder sb, object value, int maxLength, bool truncateBegin)
            {
                int startLength = sb.Length;
                sb.Append(GetHexOffsetString(value));
                TruncateStringBuilder(sb, maxLength, sb.Length - startLength, truncateBegin);

                return sb.Length - startLength;
            }
        }
    }
}