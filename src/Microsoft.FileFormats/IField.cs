// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    public interface IField
    {
        string Name { get; }
        ILayout Layout { get; }
        ILayout DeclaringLayout { get; }
        uint Offset { get; }

        object GetValue(TStruct tStruct);
        void SetValue(TStruct tStruct, object fieldValue);
    }
}
