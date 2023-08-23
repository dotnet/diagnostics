// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NETCOREAPP3_1
using System;
using System.Runtime.InteropServices;
#endif

namespace Microsoft.Diagnostics.Runtime
{
    internal abstract class CoreFunctions : PlatformFunctions
    {
#if NETCOREAPP3_1
        public override bool FreeLibrary(IntPtr handle)
        {
            NativeLibrary.Free(handle);
            return true;
        }

        public override IntPtr GetLibraryExport(IntPtr handle, string name)
        {
            _ = NativeLibrary.TryGetExport(handle, name, out IntPtr address);
            return address;
        }

        public override IntPtr LoadLibrary(string libraryPath)
        {
            return NativeLibrary.Load(libraryPath);
        }
#endif
    }
}
