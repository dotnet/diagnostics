// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal sealed class TableOutput
    {
        private readonly StringBuilder _rowBuilder = new(260);
        private readonly char _spacing = ' ';

        public string Divider { get; set; } = " ";

        public string Indent { get; set; } = "";

        public bool AlignLeft { get; set; }

        public int ColumnCount => _formats.Length;

        public IConsoleService Console { get; }

        public int TotalWidth => 1 * (_formats.Length - 1) + _formats.Sum(c => Math.Abs(c.width));

        private readonly (int width, string format)[] _formats;

        public TableOutput(IConsoleService console, params (int width, string format)[] columns)
        {
            _formats = columns.ToArray();
            Console = console;
        }

        public void WriteSpacer(char spacer)
        {
            Console.WriteLine(new string(spacer, Divider.Length * (_formats.Length - 1) + _formats.Sum(c => Math.Abs(c.width))));
        }

        public void WriteRow(params object[] columns)
        {
            _rowBuilder.Clear();
            _rowBuilder.Append(Indent);

            for (int i = 0; i < columns.Length; i++)
            {
                if (i != 0)
                {
                    _rowBuilder.Append(_spacing);
                }

                (int width, string format) = i < _formats.Length ? _formats[i] : default;
                FormatColumn(_spacing, columns[i], _rowBuilder, width, format);
            }

            Console.WriteLine(_rowBuilder.ToString());
        }

        public void WriteRowWithSpacing(char spacing, params object[] columns)
        {
            _rowBuilder.Clear();
            _rowBuilder.Append(Indent);

            for (int i = 0; i < columns.Length; i++)
            {
                if (i != 0)
                {
                    _rowBuilder.Append(spacing, Divider.Length);
                }

                (int width, string format) = i < _formats.Length ? _formats[i] : default;

                FormatColumn(spacing, columns[i], _rowBuilder, width, format);
            }

            Console.WriteLine(_rowBuilder.ToString());
        }

        private void FormatColumn(char spacing, object value, StringBuilder sb, int width, string format)
        {
            string action = null;
            string text;
            if (value is DmlExec dml)
            {
                value = dml.Text;
                if (Console.SupportsDml)
                {
                    action = dml.Action;
                }
            }

            if (string.IsNullOrWhiteSpace(format))
            {
                text = value?.ToString();
            }
            else
            {
                text = Format(value, format);
            }

            AddValue(spacing, sb, width, text ?? "", action);
        }

        private void AddValue(char spacing, StringBuilder sb, int width, string value, string action)
        {
            bool leftAlign = AlignLeft ? width > 0 : width < 0;
            width = Math.Abs(width);

            if (width == 0)
            {
                if (string.IsNullOrWhiteSpace(action))
                {
                    sb.Append(value);
                }
                else
                {
                    WriteAndClear(sb);
                    Console.WriteDmlExec(value, action);
                }
            }
            else if (value.Length > width)
            {
                if (!string.IsNullOrWhiteSpace(action))
                {
                    WriteAndClear(sb);
                }

                if (width <= 3)
                {
                    sb.Append(value, 0, width);
                }
                else if (leftAlign)
                {
                    value = value.Substring(0, width - 3);
                    sb.Append(value);
                    sb.Append("...");
                }
                else
                {
                    value = value.Substring(value.Length - (width - 3));
                    sb.Append("...");
                    sb.Append(value);
                }

                if (!string.IsNullOrWhiteSpace(action))
                {
                    WriteDmlExecAndClear(sb, action);
                }
            }
            else if (leftAlign)
            {
                if (!string.IsNullOrWhiteSpace(action))
                {
                    WriteAndClear(sb);
                    Console.WriteDmlExec(value, action);
                }
                else
                {
                    sb.Append(value);
                }

                int remaining = width - value.Length;
                if (remaining > 0)
                {
                    sb.Append(spacing, remaining);
                }
            }
            else
            {
                int remaining = width - value.Length;
                if (remaining > 0)
                {
                    sb.Append(spacing, remaining);
                }

                if (!string.IsNullOrWhiteSpace(action))
                {
                    WriteAndClear(sb);
                    Console.WriteDmlExec(value, action);
                }
                else
                {
                    sb.Append(value);
                }
            }
        }

        private void WriteDmlExecAndClear(StringBuilder sb, string action)
        {
            Console.WriteDmlExec(sb.ToString(), action);
            sb.Clear();
        }

        private void WriteAndClear(StringBuilder sb)
        {
            Console.Write(sb.ToString());
            sb.Clear();
        }

        private static string Format(object obj, string format)
        {
            if (obj is null)
            {
                return null;
            }

            if (obj is Enum)
            {
                return obj.ToString();
            }

            return obj switch
            {
                nint ni => ni.ToString(format),
                ulong ul => ul.ToString(format),
                long l => l.ToString(format),
                uint ui => ui.ToString(format),
                int i => i.ToString(format),
                nuint uni => ((ulong)uni).ToString(format),
                StringBuilder sb => sb.ToString(),
                IEnumerable<byte> bytes => string.Join("", bytes.Select(b => b.ToString("x2"))),
                string s => s,
                _ => throw new NotImplementedException(obj.GetType().ToString()),
            };
        }

        public class DmlExec
        {
            public object Text { get; }
            public string Action { get; }

            public DmlExec(object text, string action)
            {
                Text = text;
                Action = action;
            }
        }

        public sealed class DmlDumpObj : DmlExec
        {
            public DmlDumpObj(ulong address)
                : base(address, address != 0 ? $"!dumpobj /d {address:x}" : "")
            {
            }
        }

        public sealed class DmlDumpMT : DmlExec
        {
            public DmlDumpMT(ulong address)
                : base(address, address != 0 ? $"!dumpmt /d {address:x}" : "")
            {
            }
        }

        public sealed class DmlDumpDomain : DmlExec
        {
            public DmlDumpDomain(ulong address)
                : base(address, address != 0 ? $"!dumpdomain /d {address:x}" : "")
            {
            }
        }

        public sealed class DmlListNearObj : DmlExec
        {
            public DmlListNearObj(ulong address)
                : base(address, address != 0 ? $"!sos listnearobj {address:x}" : "")
            {
            }
        }

        public sealed class DmlVerifyObj : DmlExec
        {
            public DmlVerifyObj(ulong address)
                : base(address, address != 0 ? $"!verifyobj /d {address:x}" : "")
            {
            }
        }

        public sealed class DmlDumpHeap : DmlExec
        {
            public DmlDumpHeap(string text, MemoryRange range)
                : base(text, $"!dumpheap {range.Start:x} {range.End:x}")
            {
            }

            public DmlDumpHeap(ulong methodTable)
                : base(methodTable, methodTable != 0 ? $"!dumpheap -mt {methodTable:x}" : "")
            {
            }
        }

        public sealed class DmlVerifyHeap : DmlExec
        {
            public DmlVerifyHeap(string text, ClrSegment what)
                : base(text, $"!verifyheap -segment {what.Address}")
            {
            }
        }

        public sealed class DmlDumpHeapSegment : DmlExec
        {
            public DmlDumpHeapSegment(ClrSegment seg)
                : base(seg?.Address ?? 0, seg != null ? $"!dumpheap -segment {seg.Address:x}" : "")
            {
            }
        }
    }
}
