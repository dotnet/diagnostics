// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal static class ColumnKind
    {
        private static Column? s_text;
        private static Column? s_hexOffset;
        private static Column? s_hexValue;
        private static Column? s_dumpObj;

        private static int PointerLength { get; }

        public static Column Pointer { get; } = new(Align.Right, PointerLength, Formats.Pointer);
        public static Column Text => s_text ??= new(Align.Left, -1, Formats.Text);
        public static Column DumpObj => s_dumpObj ??= new(Align.Left, PointerLength, Formats.Pointer, Dml.DumpObj);
        public static Column HexValue => s_hexValue ??= new(Align.Right, PointerLength + 2, Formats.HexValue);
        public static Column HexOffset => s_hexOffset ??= new(Align.Right, 10, Formats.HexOffset);

        static ColumnKind()
        {
            int pointerSize = IntPtr.Size;

            // On Windows, a pointer will generally not be larger than 12 characters,
            // but on Linux we do not have that helpful constraint.  We set the length
            // of a pointer to be 16 to be sure we never truncate a pointer, but
            PointerLength = pointerSize == 4 ? 8 : 16;
        }
    }
}
