// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices
{
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

        public static bool IsRemoteOrDevicePath(string path)
        {
            return path?.Length >= 2 && IsDirectorySeparator(path[0]) && IsDirectorySeparator(path[1]);
        }

        public static bool IsSafeAbsoluteLocalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || IsRemoteOrDevicePath(path))
            {
                return false;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return path.Length >= 3 &&
                    IsAsciiLetter(path[0]) &&
                    path[1] == ':' &&
                    IsDirectorySeparator(path[2]);
            }

            return path[0] == '/';
        }

        private static bool IsDirectorySeparator(char character) => character is '\\' or '/';

        private static bool IsAsciiLetter(char character) => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }
}
