// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal static class HelperExtensions
    {
        public static string? GetFilename(this Stream stream)
        {
            return stream is FileStream fs ? fs.Name : null;
        }
    }
}
