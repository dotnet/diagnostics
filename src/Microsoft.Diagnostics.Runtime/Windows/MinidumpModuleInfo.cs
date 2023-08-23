// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal sealed class MinidumpModuleInfo
    {
        public ulong BaseOfImage { get; }
        public int SizeOfImage { get; }
        public int DateTimeStamp { get; }
        public FixedFileInfo VersionInfo { get; }
        public string? ModuleName { get; }

        public MinidumpModuleInfo(MinidumpMemoryReader reader, in MinidumpModule module)
        {
            BaseOfImage = module.BaseOfImage;
            SizeOfImage = module.SizeOfImage;
            DateTimeStamp = module.DateTimeStamp;
            VersionInfo = module.VersionInfo;
            ModuleName = reader.ReadCountedUnicode(module.ModuleNameRva);
        }
    }
}
