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
        private static Column? s_integer;
        private static Column? s_dumpHeapMT;
        private static Column? s_dumpHeapSegment;
        private static Column? s_listNearObj;
        private static Column? s_dumpDomain;

        private static int PointerLength => IntPtr.Size * 2;

        public static Column Pointer { get; } = new(Align.Right, PointerLength, Formats.Pointer);
        public static Column Text => s_text ??= new(Align.Left, -1, Formats.Text);
        public static Column HexValue => s_hexValue ??= new(Align.Right, PointerLength + 2, Formats.HexValue);
        public static Column HexOffset => s_hexOffset ??= new(Align.Right, 10, Formats.HexOffset);
        public static Column Integer => s_integer ??= new(Align.Right, 14, Formats.IntegerWithCommas);
        public static Column ByteCount => Integer;
        public static Column DumpObj => s_dumpObj ??= new(Align.Right, PointerLength, Formats.Pointer, Dml.DumpObj);
        public static Column DumpHeapMT => s_dumpHeapMT ??= new(Align.Right, PointerLength, Formats.Pointer, Dml.DumpHeapMT);
        public static Column DumpHeapSegment => s_dumpHeapSegment ??= new(Align.Right, PointerLength, Formats.Pointer, Dml.DumpHeapSegment);
        public static Column DumpDomain => s_dumpDomain ??= new(Align.Right, PointerLength, Formats.Pointer, Dml.DumpDomain);
        public static Column ListNearObj => s_listNearObj ??= new(Align.Right, PointerLength, Formats.Pointer, Dml.ListNearObj);

        public static Column TypeName => s_text ??= new(Align.Left, -1, Formats.TypeName);
    }
}
