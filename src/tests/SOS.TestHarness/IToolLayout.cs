// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection;

namespace SOS.TestHarness;

/// <summary>Locates product files and configures their use without preparing or installing them.</summary>
internal interface IToolLayout
{
    string SosDirectory { get; }
    string ManagedSosDirectory { get; }
    string DumpCacheDirectory { get; }
    string? CDacOverrideDirectory { get; }

    ProcessStartInfo CreateDotNetDumpStartInfo();
    void ConfigureCaptureEnvironment(ProcessStartInfo debuggee);
    Assembly LoadSosAssembly();
    void PrepareCDacOverride(Host host);
}
