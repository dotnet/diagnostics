// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>Describes the product files and working directories in a tool installation.</summary>
internal interface IToolLayout
{
    string SosDirectory { get; }
    string SosAssemblyPath { get; }
    ToolCommand DotNetDump { get; }
    string DumpCacheDirectory { get; }
    string? CDacOverrideDirectory { get; }
}
