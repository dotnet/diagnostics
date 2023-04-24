// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal sealed class Table
    {
        private readonly StringBuilderPool _stringBuilderPool = new();
        private readonly char _spacing = ' ';
        private static readonly Column s_headerColumn = new(Align.Center, -1, Formats.Text, Dml.Bold);

        public string Divider { get; set; } = " ";

        public string Indent { get; set; } = "";

        public bool AlignLeft { get; set; }

        public int ColumnCount => _columns.Length;

        public IConsoleService Console { get; }

        public int TotalWidth => 1 * (_columns.Length - 1) + _columns.Sum(c => Math.Abs(c.Width));

        private readonly Column[] _columns;

        public Table(IConsoleService console, params Column[] columns)
        {
            _columns = columns.ToArray();
            Console = console;
        }

        public void WriteHeader(params string[] values)
        {
            // Increase column width if too small
            for (int i = 0; i < _columns.Length && i < values.Length; i++)
            {
                if (_columns.Length >= 0 && values[i].Length > _columns.Length)
                {
                    if (_columns[i].Width != -1 && _columns[i].Width < values[i].Length)
                    {
                        _columns[i] = _columns[i].WithWidth(values[i].Length);
                    }
                }
            }

            StringBuilder rowBuilder = _stringBuilderPool.Rent();
            rowBuilder.Append(Indent);

            Column headerCol = s_headerColumn;
            if (!Console.SupportsDml)
            {
                headerCol = headerCol.WithDml(null);
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (i != 0)
                {
                    rowBuilder.Append(_spacing);
                }

                int width = i < _columns.Length ? _columns[i].Width : -1;
                Align align = i < _columns.Length ? _columns[i].Alignment : Align.Left;
                Append(headerCol.WithWidth(width).WithAlignment(align), rowBuilder, values[i]);
            }

            rowBuilder.Append('\n');
            if (Console.SupportsDml)
            {
                Console.WriteDml(rowBuilder.ToString());
            }
            else
            {
                Console.Write(rowBuilder.ToString());
            }

            _stringBuilderPool.Return(rowBuilder);
        }

        public void WriteRow(params object[] values)
        {
            StringBuilder rowBuilder = _stringBuilderPool.Rent();
            rowBuilder.Append(Indent);

            bool isRowBuilderDml = false;

            for (int i = 0; i < values.Length; i++)
            {
                if (i != 0)
                {
                    rowBuilder.Append(_spacing);
                }

                Column column = i < _columns.Length ? _columns[i] : ColumnKind.Text;

                bool isColumnDml = Console.SupportsDml && column.Dml is not null;
                if (isRowBuilderDml != isColumnDml)
                {
                    WriteAndClearRowBuilder(rowBuilder, isRowBuilderDml);
                    isRowBuilderDml = isColumnDml;
                }

                Append(column, rowBuilder, values[i]);
            }

            rowBuilder.Append('\n');
            WriteAndClearRowBuilder(rowBuilder, isRowBuilderDml);
            _stringBuilderPool.Return(rowBuilder);
        }

        private void WriteAndClearRowBuilder(StringBuilder rowBuilder, bool dml)
        {
            if (rowBuilder.Length != 0)
            {
                if (dml)
                {
                    Console.WriteDml(rowBuilder.ToString());
                }
                else
                {
                    Console.Write(rowBuilder.ToString());
                }

                rowBuilder.Clear();
            }
        }

        public void Append(Column column, StringBuilder sb, object value)
        {
            DmlFormat dml = null;
            if (Console.SupportsDml)
            {
                dml = column.Dml;
            }

            // Efficient case
            if (dml is null && column.Alignment == Align.Left)
            {
                int written = column.Format.FormatValue(sb, value, column.Width, column.Alignment == Align.Left);
                Debug.Assert(written >= 0);
                if (written < column.Width)
                {
                    sb.Append(' ', column.Width - written);
                }

                return;
            }

            string toWrite = column.Format.FormatValue(value, column.Width, column.Alignment == Align.Left);
            int displayLength = toWrite.Length;
            if (dml is not null)
            {
                toWrite = dml.FormatValue(toWrite, value);
            }

            if (column.Width < 0)
            {
                sb.Append(toWrite);
            }
            else
            {
                if (column.Alignment == Align.Left)
                {
                    sb.Append(toWrite);
                    if (displayLength < column.Width)
                    {
                        sb.Append(' ', column.Width - displayLength);
                    }

                    return;
                }
                else if (column.Alignment == Align.Right)
                {
                    sb.Append(' ', column.Width - displayLength);
                    sb.Append(toWrite);
                }
                else
                {
                    Debug.Assert(column.Alignment == Align.Center);

                    int remainder = column.Width - displayLength;
                    int right = remainder >> 1;
                    int left = right + (remainder % 2);

                    sb.Append(' ', left);
                    sb.Append(toWrite);
                    sb.Append(' ', right);
                }
            }
        }
    }
}
