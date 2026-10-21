// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace SOS.TestHarness;

/// <summary>A tool entry point: either an executable or a managed DLL run by a specific dotnet host.</summary>
internal sealed record ToolCommand(string FileName, string? AssemblyPath = null)
{
    public ProcessStartInfo CreateStartInfo()
    {
        ProcessStartInfo startInfo = new(FileName) { UseShellExecute = false };
        if (AssemblyPath is not null)
        {
            startInfo.ArgumentList.Add(AssemblyPath);
        }
        startInfo.Environment["DOTNET_ROOT"] = RepoLayout.DotNetRoot;
        startInfo.Environment[$"DOTNET_ROOT_{RepoLayout.TargetArch.ToUpperInvariant()}"] = RepoLayout.DotNetRoot;
        return startInfo;
    }

    public void ConfigureCaptureEnvironment(ProcessStartInfo debuggee)
    {
        debuggee.Environment["SOSHARNESS_DOTNET"] = RepoLayout.DotNetExe;
        debuggee.Environment["SOSHARNESS_DOTNETDUMP"] = FileName;
        debuggee.Environment["SOSHARNESS_DOTNETDUMP_DLL"] = AssemblyPath;
        debuggee.Environment["SOSHARNESS_DOTNET_ROOT"] = RepoLayout.DotNetRoot;
    }
}

internal static class ToolLayout
{
    internal static IToolLayout FromConfiguration(string? distribution, string? root)
    {
        if (distribution is null or "" or "Bin")
        {
            if (!string.IsNullOrEmpty(root))
            {
                throw new ArgumentException("SOSHARNESS_TOOL_ROOT is only valid with a NuGet or SingleFile layout.");
            }
            return new BinToolLayout();
        }

        if (distribution is not ("NuGet" or "SingleFile"))
        {
            throw new ArgumentException($"Invalid SOSHARNESS_TOOL_LAYOUT '{distribution}'. Expected Bin, NuGet, or SingleFile.");
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException($"SOSHARNESS_TOOL_ROOT is required for the {distribution} layout.");
        }

        return distribution == "NuGet" ? new NuGetToolLayout(root) : new SingleFileToolLayout(root);
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
