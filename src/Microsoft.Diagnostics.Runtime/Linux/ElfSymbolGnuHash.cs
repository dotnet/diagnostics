// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal sealed class ElfSymbolGnuHash
    {
        private readonly Reader _reader;
        private readonly ulong _chainsAddress;

        internal static ElfSymbolGnuHash? Create(Reader reader, bool is64Bit, ulong address)
        {
            try
            {
                return new ElfSymbolGnuHash(reader, is64Bit, address);
            }
            catch (IOException)
            {
            }
            catch (InvalidDataException)
            {
            }

            return null;
        }

        private ElfSymbolGnuHash(Reader reader, bool is64Bit, ulong address)
        {
            _reader = reader;

            BucketCount = reader.Read<int>(ref address);
            SymbolOffset = reader.Read<int>(ref address);
            BloomSize = reader.Read<int>(ref address);
            BloomShift = reader.Read<int>(ref address);

            if (BucketCount <= 0 || SymbolOffset == 0)
                throw new InvalidDataException("ELF file has a hash bucket count or symbol offset invalid");

            if (BloomSize < 0)
                throw new InvalidDataException("ELF file has a negative BloomSize.");

            long sizeTSize = is64Bit ? 8 : 4;
            address += (ulong)(sizeTSize * BloomSize);

            Buckets = new int[BucketCount];
            byte[] buffer = new byte[BucketCount * Marshal.SizeOf<int>()];
            if (reader.ReadBytes(address, new Span<byte>(buffer)) != buffer.Length)
                throw new InvalidDataException("Error reading ELF dump's bucket array");

            for (int i = 0; i < BucketCount; i++)
                Buckets[i] = BitConverter.ToInt32(buffer, i * Marshal.SizeOf<int>());

            _chainsAddress = address + (ulong)(BucketCount * (uint)Marshal.SizeOf<int>());
        }

        public int BucketCount { get; }

        public int SymbolOffset { get; }

        public int BloomSize { get; }

        public int BloomShift { get; }

        public int[] Buckets { get; }

        public IEnumerable<int> GetPossibleSymbolIndex(string symbolName)
        {
            // This implementation completely ignores the bloom filter. The results should still be correct, but may
            // be slower to determine that a missing symbol isn't in the table.
            uint hash = Hash(symbolName);
            int i = Buckets[hash % BucketCount] - SymbolOffset;
            for (; ; i++)
            {
                int chainVal = GetChain(i);
                if ((chainVal & 0xfffffffe) == (hash & 0xfffffffe))
                {
                    yield return i + SymbolOffset;
                }
                if ((chainVal & 0x1) == 0x1)
                {
                    break;
                }
            }
        }

        private static uint Hash(string symbolName)
        {
            byte[] utf8Chars = Encoding.UTF8.GetBytes(symbolName);
            uint h = 5381;
            for (int i = 0; i < utf8Chars.Length; i++)
            {
                h = unchecked((h << 5) + h + utf8Chars[i]);
            }
            return h;
        }

        private int GetChain(int index)
        {
            ulong address = (ulong)((long)_chainsAddress + index * 4);
            return _reader.Read<int>(address);
        }
    }
}
