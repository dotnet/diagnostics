// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;

namespace Microsoft.FileFormats.PE
{
    public sealed class PEPdbRecord
    {
        public bool IsPortablePDB { get; private set; }
        public string Path { get; private set; }
        public Guid Signature { get; private set; }
        public int Age { get; private set; }

        public PEPdbRecord(bool isPortablePDB, string path, Guid sig, int age)
        {
            IsPortablePDB = isPortablePDB;
            Path = path;
            Signature = sig;
            Age = age;
        }
    }
}
