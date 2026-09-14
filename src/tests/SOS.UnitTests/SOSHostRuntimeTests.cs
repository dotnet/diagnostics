// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;

public class SOSHostRuntimeTests
{
    private const string HostRuntimeAssemblyResourceName = "SOS.HostRuntimeAssemblyList";
    private const string HostRuntimeAssemblyPrefix = "HOST_RUNTIME_ASSEMBLY(\"";

    public static IEnumerable<object[]> HostRuntimeConfigurations =>
        SOSTestHelpers.GetNetCoreConfigurations()
            .Select(arguments => (TestConfiguration)arguments[0])
            .Where(config => !config.PublishSingleFile)
            .GroupBy(config => config.RuntimeFrameworkVersionMajor)
            .Select(group => new object[] { group.First() });

    [Theory]
    [MemberData(nameof(HostRuntimeConfigurations))]
    public void LayoutAssembliesNewerThanHostRuntimeAreTpaOverrides(TestConfiguration config)
    {
        string sosPath = config.SOSPath();
        string layoutDirectory = Path.GetDirectoryName(sosPath);
        Assert.True(Directory.Exists(layoutDirectory), $"SOS layout directory does not exist: {layoutDirectory}");

        string runtimeDirectory = Path.Combine(
            config.DotNetRoot,
            "shared",
            "Microsoft.NETCore.App",
            config.RuntimeFrameworkVersion);
        Assert.True(Directory.Exists(runtimeDirectory), $"Host runtime directory does not exist: {runtimeDirectory}");

        IReadOnlyDictionary<string, Version> layoutAssemblies = ReadAssemblies(layoutDirectory);
        IReadOnlyDictionary<string, Version> runtimeAssemblies = ReadAssemblies(runtimeDirectory);
        IReadOnlyDictionary<string, (int FirstRuntimeMajor, int CompatibleRuntimeMajor)> tpaOverrides = ReadTpaOverrides();
        List<string> missingOverrides = new();

        foreach ((string assemblyName, Version layoutVersion) in layoutAssemblies)
        {
            if (runtimeAssemblies.TryGetValue(assemblyName, out Version runtimeVersion) &&
                layoutVersion > runtimeVersion &&
                (!tpaOverrides.TryGetValue(assemblyName, out (int FirstRuntimeMajor, int CompatibleRuntimeMajor) runtimeRange) ||
                 config.RuntimeFrameworkVersionMajor < runtimeRange.FirstRuntimeMajor ||
                 config.RuntimeFrameworkVersionMajor >= runtimeRange.CompatibleRuntimeMajor))
            {
                missingOverrides.Add($"{assemblyName}: layout {layoutVersion}, runtime {runtimeVersion}");
            }
        }

        Assert.True(
            missingOverrides.Count == 0,
            $"The SOS layout contains assemblies newer than the .NET {config.RuntimeFrameworkVersionMajor} host runtime " +
            $"that are not selected by {HostRuntimeAssemblyResourceName}:{Environment.NewLine}" +
            string.Join(Environment.NewLine, missingOverrides));
    }

    private static IReadOnlyDictionary<string, Version> ReadAssemblies(string directory)
    {
        Dictionary<string, Version> assemblies = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            using FileStream stream = File.OpenRead(path);
            using PEReader peReader = new(stream);
            if (!peReader.HasMetadata)
            {
                continue;
            }

            MetadataReader metadataReader = peReader.GetMetadataReader();
            if (!metadataReader.IsAssembly)
            {
                continue;
            }

            AssemblyDefinition definition = metadataReader.GetAssemblyDefinition();
            assemblies[metadataReader.GetString(definition.Name)] = definition.Version;
        }

        return assemblies;
    }

    private static IReadOnlyDictionary<string, (int FirstRuntimeMajor, int CompatibleRuntimeMajor)> ReadTpaOverrides()
    {
        Dictionary<string, (int FirstRuntimeMajor, int CompatibleRuntimeMajor)> overrides = new(StringComparer.OrdinalIgnoreCase);
        Assembly assembly = typeof(SOSHostRuntimeTests).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(HostRuntimeAssemblyResourceName);
        Assert.NotNull(stream);
        using StreamReader reader = new(stream);

        while (reader.ReadLine() is string line)
        {
            string trimmedLine = line.Trim();
            if (!trimmedLine.StartsWith(HostRuntimeAssemblyPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            int fileNameStart = HostRuntimeAssemblyPrefix.Length;
            int fileNameEnd = trimmedLine.IndexOf('"', fileNameStart);
            int firstRuntimeMajorStart = trimmedLine.IndexOf(',', fileNameEnd) + 1;
            int firstRuntimeMajorEnd = trimmedLine.IndexOf(',', firstRuntimeMajorStart);
            int compatibleRuntimeMajorStart = firstRuntimeMajorEnd + 1;
            int compatibleRuntimeMajorEnd = trimmedLine.IndexOf(')', compatibleRuntimeMajorStart);

            Assert.True(
                fileNameEnd > fileNameStart &&
                firstRuntimeMajorStart > 0 &&
                firstRuntimeMajorEnd > firstRuntimeMajorStart &&
                compatibleRuntimeMajorEnd > compatibleRuntimeMajorStart,
                $"Invalid host runtime assembly entry: {line}");

            string fileName = trimmedLine[fileNameStart..fileNameEnd];
            string assemblyName = Path.GetFileNameWithoutExtension(fileName);
            Assert.True(
                int.TryParse(trimmedLine[firstRuntimeMajorStart..firstRuntimeMajorEnd], out int firstRuntimeMajor),
                $"Invalid first host runtime version: {line}");
            Assert.True(
                int.TryParse(trimmedLine[compatibleRuntimeMajorStart..compatibleRuntimeMajorEnd], out int compatibleRuntimeMajor),
                $"Invalid compatible host runtime version: {line}");
            overrides.Add(assemblyName, (firstRuntimeMajor, compatibleRuntimeMajor));
        }

        return overrides;
    }
}
