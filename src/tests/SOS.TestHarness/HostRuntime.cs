// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>Whether SOS hosts its managed extension runtime.</summary>
[Flags]
public enum HostRuntime
{
    /// <summary>Enable the managed SOS extension using the harness's configured host runtime.</summary>
    Managed = 1,

    /// <summary>Disable managed hosting with <c>sethostruntime -none</c>.</summary>
    None = 2,

    /// <summary>Both managed-hosting modes.</summary>
    AllValid = Managed | None,
}
