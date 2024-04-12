// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.IO.Compression;
using System.Linq;

namespace TestHelpers
{
    public static class TestUtilities
    {
        public static Stream OpenCompressedFile(string path)
        {
            MemoryStream ms = new();
            using (FileStream fs = File.OpenRead(path))
            {
                using (GZipStream gs = new(fs, CompressionMode.Decompress))
                {
                    gs.CopyTo(ms);
                }
            }
            return ms;
        }

        public static Stream DecompressFile(string source, string destination)
        {
            bool fileExists = File.Exists(destination);
            FileStream destStream = File.Open(destination, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            if (!fileExists || destStream.Length == 0)
            {
                using (FileStream s = File.OpenRead(source))
                {
                    using (GZipStream gs = new(s, CompressionMode.Decompress))
                    {
                        gs.CopyTo(destStream);
                    }
                    destStream.Position = 0;
                }
            }
            return destStream;
        }

        /// <summary>
        /// Convert an array of bytes to a lower case hex string.
        /// </summary>
        /// <param name="bytes">array of bytes</param>
        /// <returns>hex string</returns>
        public static string ToHexString(byte[] bytes)
        {
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }
    }
}
