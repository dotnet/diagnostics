// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>A tool entry point: either an executable or a managed DLL run by a specific dotnet host.</summary>
internal sealed record ToolCommand(string FileName, string? AssemblyPath = null);

internal static class ToolLayout
{
    internal static IToolLayout FromConfiguration(string? distribution, string? root)
    {
        if (distribution is null or "" or "Bin")
        {
            if (!string.IsNullOrEmpty(root))
            {
                throw new ArgumentException("SOSHARNESS_TOOL_ROOT is only valid with a Packaged layout.");
            }
            return new BinToolLayout();
        }

        if (distribution != "Packaged")
        {
            throw new ArgumentException($"Invalid SOSHARNESS_TOOL_LAYOUT '{distribution}'. Expected Bin or Packaged.");
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException($"SOSHARNESS_TOOL_ROOT is required for the {distribution} layout.");
        }

        return new PackagedToolLayout(root);
    }

    internal static string RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required SOS test tool input was not found at '{path}'.", path);
        }
        return path;
    }
}
