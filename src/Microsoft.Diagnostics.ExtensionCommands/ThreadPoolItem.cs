﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public class ThreadPoolItem
    {
        public ThreadRoot Type { get; set; }
        public ulong Address { get; set; }
        public string MethodName { get; set; }
    }
}
