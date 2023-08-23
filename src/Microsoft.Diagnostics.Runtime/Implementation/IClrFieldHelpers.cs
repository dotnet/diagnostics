// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal interface IClrFieldHelpers
    {
        IDataReader DataReader { get; }
        bool ReadProperties(ClrType parentType, int token, out string? name, out FieldAttributes attributes, ref ClrType? type);
        ulong GetStaticFieldAddress(ClrStaticField field, ulong appDomain);
    }
}