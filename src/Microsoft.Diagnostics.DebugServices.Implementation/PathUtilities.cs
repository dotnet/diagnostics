// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    // Ported from microsoft/clrmd PathUtilities at cba085b49b816c88ea5ca80327c0226ed4141a74.
    internal static class PathUtilities
    {
        private static readonly char[] s_directorySeparators = ['\\', '/'];

        public static string GetFileName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }
            int index = path.LastIndexOfAny(s_directorySeparators);
            return index >= 0 ? path.Substring(index + 1) : path;
        }

        public static bool IsSafeAbsoluteLocalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                (path.Length >= 2 && IsDirectorySeparator(path[0]) && IsDirectorySeparator(path[1])))
            {
                return false;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return path.Length >= 3 && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
                    && path[1] == ':' && IsDirectorySeparator(path[2]);
            }
            return path[0] == '/';
        }

        private static bool IsDirectorySeparator(char value) => value is '\\' or '/';
    }
}
