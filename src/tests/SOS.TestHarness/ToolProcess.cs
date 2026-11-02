// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace SOS.TestHarness;

internal static class ToolProcess
{
    public static ProcessStartInfo CreateStartInfo(ToolCommand command, string? bundleExtractionDirectory = null)
    {
        ProcessStartInfo startInfo = new(command.FileName) { UseShellExecute = false };
        if (command.AssemblyPath is not null)
        {
            startInfo.ArgumentList.Add(command.AssemblyPath);
        }
        startInfo.Environment["DOTNET_ROOT"] = RepoLayout.DotNetRoot;
        startInfo.Environment[$"DOTNET_ROOT_{RepoLayout.TargetArch.ToUpperInvariant()}"] = RepoLayout.DotNetRoot;
        ConfigureBundleExtraction(startInfo, bundleExtractionDirectory);
        return startInfo;
    }

    public static void ConfigureCaptureEnvironment(ToolCommand command, ProcessStartInfo debuggee, string? bundleExtractionDirectory = null)
    {
        debuggee.Environment["SOSHARNESS_DOTNET"] = RepoLayout.DotNetExe;
        debuggee.Environment["SOSHARNESS_DOTNETDUMP"] = command.FileName;
        debuggee.Environment["SOSHARNESS_DOTNETDUMP_DLL"] = command.AssemblyPath;
        debuggee.Environment["SOSHARNESS_DOTNET_ROOT"] = RepoLayout.DotNetRoot;
        ConfigureBundleExtraction(debuggee, bundleExtractionDirectory);
    }

    private static void ConfigureBundleExtraction(ProcessStartInfo process, string? directory)
    {
        if (directory is not null)
        {
            process.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = directory;
        }
    }
}
