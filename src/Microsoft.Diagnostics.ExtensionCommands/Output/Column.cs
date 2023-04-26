// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal readonly struct Column
    {
        private static readonly StringBuilderPool s_stringBuilderPool = new();
        private static readonly Column s_enum = new(Align.Left, -1, new(), null);

        public readonly int Width;
        public readonly Format Format;
        public readonly Align Alignment;
        public readonly DmlFormat Dml;

        public Column(Align alignment, int width, Format format, DmlFormat dml = null)
        {
            Alignment = alignment;
            Width = width;
            Format = format ?? throw new ArgumentNullException(nameof(format));
            Dml = dml;
        }

        public readonly Column WithWidth(int width) => new(Alignment, width, Format, Dml);
        internal readonly Column WithDml(DmlFormat dml) => new(Alignment, Width, Format, dml);
        internal readonly Column WithAlignment(Align align) => new(align, Width, Format, Dml);

        public readonly Column GetAppropriateWidth<T>(IEnumerable<T> values, int min = -1, int max = -1)
        {
            int len = 0;

            StringBuilder sb = s_stringBuilderPool.Rent();

            foreach (T value in values)
            {
                sb.Clear();
                Format.FormatValue(sb, value, -1, false);
                len = Math.Max(len, sb.Length);
            }

            s_stringBuilderPool.Return(sb);

            if (len < min)
            {
                len = min;
            }

            if (max > 0 && len > max)
            {
                len = max;
            }

            return WithWidth(len);
        }

        internal static Column ForEnum<TEnum>()
            where TEnum : struct
        {
            int len = 0;
            foreach (TEnum t in Enum.GetValues(typeof(TEnum)))
            {
                len = Math.Max(len, t.ToString().Length);
            }

            return s_enum.WithWidth(len);
        }

        public override string ToString()
        {
            string format = Format?.GetType().Name ?? "null";
            string dml = Dml?.GetType().Name ?? "null";

            return $"align:{Alignment} width:{Width} format:{format} dml:{dml}";
        }
    }
}
