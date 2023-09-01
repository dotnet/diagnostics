// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal static class ColumnKind
    {
        private static Column? s_pointer;
        private static Column? s_text;
        private static Column? s_image;
        private static Column? s_typeName;
        private static Column? s_hexOffset;
        private static Column? s_hexValue;
        private static Column? s_dumpObj;
        private static Column? s_integer;
        private static Column? s_dumpHeapMT;
        private static Column? s_listNearObj;
        private static Column? s_dumpDomain;
        private static Column? s_thread;
        private static Column? s_integerWithoutComma;
        private static Column? s_humanReadable;
        private static Column? s_range;

        // NOTE/BUGBUG: This assumes IntPtr.Size matches the target process, which it should not do
        private static int PointerLength => IntPtr.Size * 2;

        /// <summary>
        /// A pointer, displayed as hex.
        /// </summary>
        public static Column Pointer => s_pointer ??= new(Align.Right, PointerLength, Formats.Pointer);

        /// <summary>
        /// Raw text which will not be truncated by default.
        /// </summary>
        public static Column Text => s_text ??= new(Align.Left, -1, Formats.Text);

        /// <summary>
        /// A hex value, prefixed with 0x.
        /// </summary>
        public static Column HexValue => s_hexValue ??= new(Align.Right, PointerLength + 2, Formats.HexValue);

        /// <summary>
        /// An offset (potentially negative), prefixed with 0x.  For example: '0x20' or '-0x20'.
        /// </summary>
        public static Column HexOffset => s_hexOffset ??= new(Align.Right, 10, Formats.HexOffset);

        /// <summary>
        /// An integer, with commas.  i.e. i.ToString("n0")
        /// </summary>
        public static Column Integer => s_integer ??= new(Align.Right, 14, Formats.Integer);

        /// <summary>
        /// An integer, without commas.
        /// </summary>
        public static Column IntegerWithoutCommas => s_integerWithoutComma ??= new(Align.Right, 10, Formats.IntegerWithoutCommas);

        /// <summary>
        /// A count of bytes (size).
        /// </summary>
        public static Column ByteCount => Integer;

        /// <summary>
        /// A human readable size count.  e.g. "1.23mb"
        /// </summary>
        public static Column HumanReadableSize => s_humanReadable ??= new(Align.Right, 12, Formats.HumanReadableSize);

        /// <summary>
        /// An object pointer, which we would like to link to !do if Dml is enabled.
        /// </summary>
        public static Column DumpObj => s_dumpObj ??= new(Align.Right, PointerLength, Formats.Pointer, Dml.DumpObj);

        /// <summary>
        /// A link to any number of ClrMD objects (ClrSubHeap, ClrSegment, a MethodTable or ClrType, etc) which will
        /// print an appropriate !dumpheap filter for, if dml is enabled.
        /// </summary>
        public static Column DumpHeap => s_dumpHeapMT ??= new(Align.Right, PointerLength, Formats.Pointer, Dml.DumpHeap);

        /// <summary>
        /// A link to !dumpdomain for the given domain, if dml is enabled.  This also puts the domain's name in the
        /// hover text for the link.
        /// </summary>
        public static Column DumpDomain => s_dumpDomain ??= new(Align.Right, PointerLength, Formats.Pointer, Dml.DumpDomain);

        /// <summary>
        /// The ClrThread address with a link to the OSThreadID to change threads (if dml is enabled).
        /// </summary>
        public static Column Thread => s_thread ??= new(Align.Right, PointerLength, Formats.Pointer, Dml.Thread);

        /// <summary>
        /// A link to !listnearobj for the given ClrObject or address, if dml is enabled.
        /// </summary>
        public static Column ListNearObj => s_listNearObj ??= new(Align.Right, PointerLength, Formats.Pointer, Dml.ListNearObj);

        /// <summary>
        /// The name of a given type.  Note that types are always truncated by removing the beginning of the type's
        /// name instead of truncating based on alignment.  This ensures the most important part of the name (the
        /// actual type name) is preserved instead of the namespace.
        /// </summary>
        public static Column TypeName => s_typeName ??= new(Align.Left, -1, Formats.TypeName);

        /// <summary>
        /// A path to an image on disk.  Note that images are always truncted by removing the beginning of the image's
        /// path instead of the end, preserving the filename.
        /// </summary>
        public static Column Image => s_image ??= new(Align.Left, -1, Formats.Image);

        /// <summary>
        /// A MemoryRange printed as "[start-end]".
        /// </summary>
        public static Column Range => s_range ??= new(Align.Left, PointerLength * 2 + 1, Formats.Range);
    }
}
