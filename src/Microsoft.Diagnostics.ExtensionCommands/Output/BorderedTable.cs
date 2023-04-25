// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal sealed class BorderedTable : Table, IDisposable
    {
        private bool _wroteAtLeastOneSpacer;

        public BorderedTable(IConsoleService console, params Column[] columns)
            : base(console, columns)
        {
            _spacing = $" | ";
        }

        public void Dispose()
        {
            WriteSpacer();
        }

        public override void WriteHeader(params string[] values)
        {
            IncreaseColumnWidth(values);

            WriteSpacer();
            WriteHeaderFooter(values, writeSides: true, writeNewline: true);
            WriteSpacer();
        }

        public override void WriteFooter(params object[] values)
        {
            WriteSpacer();
            WriteHeaderFooter(values, writeSides: true, writeNewline: true);
        }

        public override void WriteRow(params object[] values)
        {
            // Ensure the top of the table is written even if there's no header/footer.
            if (!_wroteAtLeastOneSpacer)
            {
                WriteSpacer();
            }

            StringBuilder rowBuilder = _stringBuilderPool.Rent();
            rowBuilder.Append(Indent);
            rowBuilder.Append(_spacing);

            WriteRowWorker(values, rowBuilder, _spacing, writeLine: false);
            rowBuilder.Append(_spacing);

            FinishColumns(values.Length, rowBuilder);

            Console.WriteLine(rowBuilder.ToString());
            _stringBuilderPool.Return(rowBuilder);
        }

        protected override void WriteHeaderFooter(object[] values, bool writeSides, bool writeNewline)
        {
            base.WriteHeaderFooter(values, writeSides, writeNewline: false);

            StringBuilder rowBuilder = _stringBuilderPool.Rent();
            FinishColumns(values.Length, rowBuilder);

            if (writeNewline)
            {
                rowBuilder.AppendLine();
            }

            Console.Write(rowBuilder.ToString());
            _stringBuilderPool.Return(rowBuilder);
        }

        private void FinishColumns(int start, StringBuilder rowBuilder)
        {
            for (int i = start; i < Columns.Length; i++)
            {
                if (Columns[i].Width < 0)
                {
                    break;
                }

                rowBuilder.Append(' ', Columns[i].Width);
                rowBuilder.Append(_spacing);
            }
        }

        private void WriteSpacer()
        {
            WriteBorder(" +-", '-', "-+ ");
            _wroteAtLeastOneSpacer = true;
        }

        private void WriteBorder(string left, char center, string right)
        {
            StringBuilder rowBuilder = _stringBuilderPool.Rent();
            rowBuilder.Append(Indent);

            rowBuilder.Append(left);

            for (int i = 0; i < Columns.Length; i++)
            {
                if (i != 0)
                {
                    rowBuilder.Append(center, _spacing.Length);
                }

                rowBuilder.Append(center, Columns[i].Width);
            }

            rowBuilder.Append(right);
            Console.WriteLine(rowBuilder.ToString());

            _stringBuilderPool.Return(rowBuilder);
        }
    }
}
