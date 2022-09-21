// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_VALUE_TYPE : uint
    {
        INVALID = 0,
        INT8 = 1,
        INT16 = 2,
        INT32 = 3,
        INT64 = 4,
        FLOAT32 = 5,
        FLOAT64 = 6,
        FLOAT80 = 7,
        FLOAT82 = 8,
        FLOAT128 = 9,
        VECTOR64 = 10,
        VECTOR128 = 11,
        TYPES = 12
    }
}