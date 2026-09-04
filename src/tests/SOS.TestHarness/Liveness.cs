// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

[Flags]
public enum Liveness
{
    Live = 1,
    Dump = 2,
    AllValid = Live | Dump,
}
