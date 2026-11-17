// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;
using Xunit.Sdk;
using Xunit.v3;

[assembly: TestPipelineStartup(typeof(SOS.Tests.PackagedToolSetup))]

namespace SOS.Tests;

public sealed class PackagedToolSetup : ITestPipelineStartup
{
    public async ValueTask StartAsync(IMessageSink diagnosticMessageSink)
    {
        string? source = Environment.GetEnvironmentVariable("SOSHARNESS_PACKAGE_DIRECTORY");
        if (string.IsNullOrEmpty(source))
        {
            return;
        }

        if (Environment.GetEnvironmentVariable("SOSHARNESS_TOOL_LAYOUT") != "Packaged")
        {
            throw new InvalidOperationException("SOSHARNESS_PACKAGE_DIRECTORY requires the Packaged tool layout.");
        }

        string root = Environment.GetEnvironmentVariable("SOSHARNESS_TOOL_ROOT")
            ?? throw new InvalidOperationException("SOSHARNESS_TOOL_ROOT is required to install test packages.");
        string dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? throw new InvalidOperationException("DOTNET_ROOT must identify the Helix-provisioned SDK.");
        await InstallAsync(source, root, dotnetRoot);
    }

    public ValueTask StopAsync() => ValueTask.CompletedTask;

    internal static string GetPackageVersion(string source)
    {
        string[] packages = Directory.GetFiles(source, "dotnet-sos.*.nupkg");
        if (packages.Length != 1)
        {
            throw new InvalidDataException($"Expected exactly one dotnet-sos package in '{source}', found {packages.Length}.");
        }

        string version = Path.GetFileNameWithoutExtension(packages[0])["dotnet-sos.".Length..];
        string dump = Path.Combine(source, $"dotnet-dump.{version}.nupkg");
        if (!File.Exists(dump))
        {
            throw new FileNotFoundException("The matching dotnet-dump package was not found.", dump);
        }
        return version;
    }

    internal static async Task InstallAsync(string source, string root, string dotnetRoot)
    {
        string version = GetPackageVersion(source);
        if (Directory.Exists(root))
        {
            throw new IOException($"Tool installation directory '{root}' already exists.");
        }

        string suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        string dotnet = Path.Combine(dotnetRoot, "dotnet" + suffix);
        string tools = Path.Combine(root, "tools");
        Directory.CreateDirectory(root);
        string config = Path.Combine(root, "tool-install.config");
        new XDocument(
            new XElement("configuration",
                new XElement("packageSources",
                    new XElement("clear"),
                    new XElement("add", new XAttribute("key", "candidate"), new XAttribute("value", Path.GetFullPath(source))))))
            .Save(config);

        Dictionary<string, string?> environment = new()
        {
            ["DOTNET_ROOT"] = dotnetRoot,
            ["DOTNET_ROOT_" + RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant()] = dotnetRoot,
            ["NUGET_PACKAGES"] = Path.Combine(root, "packages"),
            ["DOTNET_CLI_HOME"] = Path.Combine(root, "dotnet-home"),
            ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
            ["DOTNET_NOLOGO"] = "true",
        };
        foreach (string tool in new[] { "dotnet-sos", "dotnet-dump" })
        {
            await RunAsync(dotnet, ["tool", "install", tool, "--version", version, "--tool-path", tools, "--configfile", config], environment);
        }

        await RunAsync(Path.Combine(tools, "dotnet-sos" + suffix), ["install", "--help"], environment);
        await RunAsync(Path.Combine(tools, "dotnet-dump" + suffix), ["--version"], environment);

        string[] helpers = Directory.GetFiles(Path.Combine(tools, ".store", "dotnet-sos"), "SOS.InstallHelper.dll", SearchOption.AllDirectories);
        if (helpers.Length != 1)
        {
            throw new InvalidDataException($"Expected one shipped SOS.InstallHelper.dll, found {helpers.Length}.");
        }

        string sos = Path.Combine(root, "sos");
        AssemblyLoadContext context = new("SOS package installer", isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(helpers[0]);
            Type type = assembly.GetType("SOS.InstallHelper", throwOnError: true)!;
            object installer = Activator.CreateInstance(type, [new Action<string>(Console.WriteLine), null])
                ?? throw new InvalidOperationException("Could not create the shipped SOS installer.");
            PropertyInfo nativeSource = RequireProperty(type, "SOSNativeSourcePath");
            string defaultSource = nativeSource.GetValue(installer) as string
                ?? throw new InvalidDataException("The shipped SOS installer did not provide a native source path.");
            string packageRoot = Path.GetDirectoryName(helpers[0])!;
            nativeSource.SetValue(installer, Path.Combine(packageRoot, Path.GetFileName(defaultSource)));
            RequireProperty(type, "SOSManagedSourcePath").SetValue(installer, Path.Combine(packageRoot, "lib"));
            RequireProperty(type, "InstallLocation").SetValue(installer, sos);
            RequireProperty(type, "LLDBInitFile").SetValue(installer, null);
            MethodInfo install = type.GetMethod("Install")
                ?? throw new MissingMethodException(type.FullName, "Install");
            install.Invoke(installer, null);
        }
        finally
        {
            context.Unload();
        }

        string nativeName = OperatingSystem.IsWindows() ? "sos.dll" :
            OperatingSystem.IsMacOS() ? "libsosplugin.dylib" : "libsosplugin.so";
        foreach (string file in new[] { Path.Combine(sos, nativeName), Path.Combine(sos, "net8.0", "SOS.Extensions.dll") })
        {
            if (!File.Exists(file))
            {
                throw new FileNotFoundException("The installed SOS payload is incomplete.", file);
            }
        }

        await File.WriteAllTextAsync(Path.Combine(root, "layout.json"), JsonSerializer.Serialize(new {
            Layout = "Packaged",
            Source = Path.GetFullPath(source),
            PackageVersion = version,
            Installer = helpers[0],
        }));
    }

    private static PropertyInfo RequireProperty(Type type, string name) =>
        type.GetProperty(name) ?? throw new MissingMemberException(type.FullName, name);

    private static async Task RunAsync(string executable, string[] arguments, Dictionary<string, string?> environment)
    {
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        foreach (KeyValuePair<string, string?> variable in environment)
        {
            start.Environment[variable.Key] = variable.Value;
        }

        Console.WriteLine($"Preparing installed SOS tools: {executable} {string.Join(" ", arguments)}");
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Console.WriteLine(await stdout);
            Console.Error.WriteLine(await stderr);
            throw new TimeoutException($"Preparing installed SOS tools timed out: '{executable}'.");
        }

        Console.WriteLine(await stdout);
        Console.Error.WriteLine(await stderr);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Preparing installed SOS tools failed: '{executable}' exited with code {process.ExitCode}.");
        }
    }
}
