// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>Shared helpers for locating specific heap objects in tests.</summary>
internal static class TestObjectHelpers
{
    /// <summary>
    /// The first instance of an exactly-named type. A <c>dumpheap -type</c> filter is a single token and a
    /// substring match, so it also catches nested/derived types and can't carry the spaces in a generic
    /// name; this filters on the space-free prefix, then selects the row whose full class name matches.
    /// </summary>
    public static ulong FirstObjectOfExactType(this Target target, string typeName)
    {
        string prefix = typeName.Split('<')[0];
        SosRow row = target.DumpHeap($"-type {prefix}").Statistics
            .SingleRow(r => r["Class Name"].Value == typeName, $"a single {typeName} method table");
        ulong mt = row["MT"].AsUInt64(Sos.Addr);
        IReadOnlyList<ulong> addresses = target.DumpHeap($"-mt {mt:x} -short").ShortAddresses;
        Assert.NotEmpty(addresses);
        return addresses[0];
    }
}
