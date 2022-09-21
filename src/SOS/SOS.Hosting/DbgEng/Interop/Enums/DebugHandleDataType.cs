// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_HANDLE_DATA_TYPE : uint
    {
        BASIC = 0,
        TYPE_NAME = 1,
        OBJECT_NAME = 2,
        HANDLE_COUNT = 3,
        TYPE_NAME_WIDE = 4,
        OBJECT_NAME_WIDE = 5,
        MINI_THREAD_1 = 6,
        MINI_MUTANT_1 = 7,
        MINI_MUTANT_2 = 8,
        PER_HANDLE_OPERATIONS = 9,
        ALL_HANDLE_OPERATIONS = 10,
        MINI_PROCESS_1 = 11,
        MINI_PROCESS_2 = 12
    }
}