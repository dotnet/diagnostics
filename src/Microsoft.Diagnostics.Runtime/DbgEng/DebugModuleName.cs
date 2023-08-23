// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.DbgEng
{
    internal enum DebugModuleName : uint
    {
        Image,
        Module,
        LoadedImage,
        SymbolFile,
        MappedImage,
    }
}