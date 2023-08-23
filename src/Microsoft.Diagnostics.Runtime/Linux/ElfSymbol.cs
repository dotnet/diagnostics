// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal sealed class ElfSymbol
    {
        public ElfSymbol(string name, ElfSymbolBind bind, ElfSymbolType type, long value, long size)
        {
            Name = name;
            Bind = bind;
            Type = type;
            Value = value;
            Size = size;
        }

        public string Name { get; }

        public ElfSymbolBind Bind { get; }

        public ElfSymbolType Type { get; }

        public long Value { get; }

        public long Size { get; }
    }
}
