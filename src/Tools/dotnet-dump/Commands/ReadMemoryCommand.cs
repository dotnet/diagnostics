// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using System;
using System.Text;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "readmemory", Aliases = new string[] { "d" }, Help = "Dump memory contents.")]
    [Command(Name = "db", DefaultOptions = "--ascii:true  --unicode:false --ascii-string:false --unicode-string:false -c:128 -l:1  -w:16", Help = "Dump memory as bytes.")]
    [Command(Name = "dc", DefaultOptions = "--ascii:false --unicode:true  --ascii-string:false --unicode-string:false -c:64  -l:2  -w:8",  Help = "Dump memory as chars.")]
    [Command(Name = "da", DefaultOptions = "--ascii:false --unicode:false --ascii-string:true  --unicode-string:false -c:128 -l:1  -w:0",  Help = "Dump memory as zero-terminated byte strings.")]
    [Command(Name = "du", DefaultOptions = "--ascii:false --unicode:false --ascii-string:false --unicode-string:true  -c:128 -l:2  -w:0",  Help = "Dump memory as zero-terminated char strings.")]
    [Command(Name = "dw", DefaultOptions = "--ascii:false --unicode:false --ascii-string:false --unicode-string:false -c:128 -l:2  -w:0",  Help = "Dump memory as words (ushort).")]
    [Command(Name = "dd", DefaultOptions = "--ascii:false --unicode:false --ascii-string:false --unicode-string:false -c:64  -l:4  -w:0",  Help = "Dump memory as dwords (uint).")]
    [Command(Name = "dp", DefaultOptions = "--ascii:false --unicode:false --ascii-string:false --unicode-string:false -c:32  -l:-1 -w:0",  Help = "Dump memory as pointers.")]
    [Command(Name = "dq", DefaultOptions = "--ascii:false --unicode:false --ascii-string:false --unicode-string:false -c:32  -l:8  -w:0",  Help = "Dump memory as qwords (ulong).")]
    public sealed class ReadMemoryCommand : CommandBase
    {
        [Argument(Name = "address", Help = "Address to dump.")]
        public string AddressValue
        {
            get => Address?.ToString();
            set => Address = ParseAddress(value);
        }

        public ulong? Address { get; set; }

        [Option(Name = "--end", Aliases = new string[] { "-e" }, Help = "Ending address to dump.")]
        public string EndAddressValue
        {
            get => EndAddress?.ToString();
            set => EndAddress = ParseAddress(value);
        }

        public ulong? EndAddress { get; set; }

        // ****************************************************************************************
        // The following arguments are static so they are remembered for the "d" command. This 
        // allows a command like "db <address>" to be followed by "d" and the next block is dumped 
        // in the remembered format.

        [Option(Name = "--count", Aliases = new string[] { "-c" }, Help = "Number of elements to dump.")]
        public static int Count { get; set; } = 32;

        [Option(Name = "--length", Aliases = new string[] { "-l" }, Help = "Size of elements to dump.")]
        public static int Length { get; set; } = 0;

        [Option(Name = "--width", Aliases = new string[] { "-w" }, Help = "Number of elements to dump per row.")]
        public static int Width { get; set; } = 0;

        [Option(Name = "--ascii", Aliases = new string[] { "-a" }, Help = "Print ascii dump as well.")]
        public static bool Ascii { get; set; } = false;

        [Option(Name = "--unicode", Aliases = new string[] { "-u" }, Help = "Print unicode dump as well.")]
        public static bool Unicode { get; set; } = false;

        [Option(Name = "--ascii-string", Help = "Print as ascii string as well.")]
        public static bool AsciiString { get; set; } = false;

        [Option(Name = "--unicode-string", Help = "Print as unicode string as well.")]
        public static bool UnicodeString { get; set; } = false;

        // ****************************************************************************************

        [Option(Name = "--hex-prefix", Aliases = new string[] { "-h" }, Help = "Add a hex prefix (0x) to the data displayed.")]
        public bool HexPrefix { get; set; } = false;

        [Option(Name = "--show-address", Help = "Display the addresses of data found.")]
        public bool ShowAddress { get; set; } = true;

        public IMemoryService MemoryService { get; set; }

        public ITarget Target { get; set; }

        private static ulong _lastAddress;

        public override void Invoke()
        {
            if (Address.HasValue) {
                _lastAddress = Address.Value;
            }

            int length = Length;
            if (length < 0) {
                length = MemoryService.PointerSize;
            }
            switch (length) {
                case 1:
                case 2:
                case 4:
                case 8:
                    break;

                default:
                    length = 4;
                    break;
            }
            Length = length;

            if (EndAddress.HasValue) {
                if (EndAddress.Value <= _lastAddress) {
                    throw new ArgumentException("Cannot dump a negative range");
                }
                int range = (int)(EndAddress.Value - _lastAddress);
                Count = range / length;
            }

            if (AsciiString || UnicodeString)
            {
                var sb = new StringBuilder();
                while (true)
                {
                    char ch = ReadChar(_lastAddress, UnicodeString, true);
                    _lastAddress += (ulong)(UnicodeString ? 2 : 1);
                    if (ch == 0)
                    {
                        break;
                    }
                    sb.Append(ch);

                    Console.CancellationToken.ThrowIfCancellationRequested();
                }
                WriteLine("Text: '{0}'", sb.ToString());
            }
            else
            {
                int count = Count > 0 ? Count : 32;
                Count = count;

                int width = Width > 0 ? Width : 32 / length;
                Width = width;

                count *= length;
                ulong address = _lastAddress;
                var sb = new StringBuilder();

                while (count > 0)
                {
                    if (ShowAddress)
                    {
                        sb.AppendFormat("{0:x16}:", address);
                    }
                    for (int column = 0; column < width; column++)
                    {
                        int offset = column * length;
                        sb.Append(" ");

                        if (offset < count)
                        {
                            byte[] data = new byte[length];

                            if (!MemoryService.ReadMemory(address + (ulong)offset, data, length, out int bytesRead))
                            {
                                data = Array.Empty<byte>();
                            }

                            if (bytesRead >= length)
                            {
                                if (HexPrefix)
                                {
                                    sb.Append("0x");
                                }
                                switch (length)
                                {
                                    case 1:
                                        sb.AppendFormat("{0:x2}", data[0]);
                                        break;

                                    case 2:
                                        sb.AppendFormat("{0:x4}", BitConverter.ToUInt16(data, 0));
                                        break;

                                    case 4:
                                        sb.AppendFormat("{0:x8}", BitConverter.ToUInt32(data, 0));
                                        break;

                                    case 8:
                                        sb.AppendFormat("{0:x16}", BitConverter.ToUInt64(data, 0));
                                        break;
                                }
                            }
                            else
                            {
                                if (HexPrefix)
                                {
                                    sb.Append("  ");
                                }
                                sb.Append('?', length * 2);
                            }
                        }
                        else
                        {
                            if (HexPrefix)
                            {
                                sb.Append("  ");
                            }
                            sb.Append(' ', length * 2);
                        }
                    }

                    if (Ascii || Unicode)
                    {
                        sb.Append("  ");
                        for (int column = 0; column < width; column++)
                        {
                            int offset = column * length;
                            if (offset < count)
                            {
                                char val = ReadChar(address + (ulong)offset, Unicode, false);
                                sb.Append(val);
                            }
                        }
                    }

                    address += (ulong)(width * length);
                    count -= width * length;

                    WriteLine(sb.ToString());
                    sb.Clear();

                    Console.CancellationToken.ThrowIfCancellationRequested();
                }

                _lastAddress = address;
            }
        }

        private char ReadChar(ulong address, bool unicode, bool zeroOk)
        {
            char value;

            int size = unicode ? 2 : 1;
            byte[] buffer = new byte[size];
            if (MemoryService.ReadMemory(address, buffer, size, out int bytesRead) && bytesRead >= size)
            {
                if (unicode)
                {
                    value = BitConverter.ToChar(buffer, 0);
                }
                else
                {
                    value = (char)buffer[0];
                }
            }
            else
            {
                value = '?';
            }

            if (value == 0 && zeroOk) {
                return value;
            }

            if (value < 0x20 || value > 0x7E) {
                value = '.';
            }

            return value;
        }
    }
}
