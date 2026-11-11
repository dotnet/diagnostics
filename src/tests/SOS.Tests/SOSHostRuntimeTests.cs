// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using SOS.Hosting.Loader;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class SOSHostRuntimeTests(ITestOutputHelper output)
{
    public static TheoryData<TestConfig, int> HostRuntimeConfigurations => Configurations(Host.AllValid);
    public static TheoryData<TestConfig, int> CdbConfigurations => Configurations(Host.Cdb);

    private static TheoryData<TestConfig, int> Configurations(Host host)
    {
        TheoryData<TestConfig, int> data = new();
        foreach (TestConfig config in TestConfig.Permutations([TargetCatalog.DivZero], Flavor.Core, host, dac: Dac.Legacy)
            .DistinctBy(config => config.CoreVersion))
        {
            data.Add(config, 1);
            data.Add(config, 99);
        }
        return data;
    }

    [SosTheory(SkipTestWithoutData = true)]
    [MemberData(nameof(HostRuntimeConfigurations))]
    public async Task IsolatesPreviouslyLoadedDependencies(TestConfig config, int conflictVersion)
    {
        string directory = GetLayoutDirectory();
        Fixture fixture = CreateFixture(nameof(IsolatesPreviouslyLoadedDependencies), config, conflictVersion);
        ProcessStartInfo start = new(RepoLayout.DotnetTestExe);
        foreach (string argument in new[]
        {
            "--fx-version", RuntimeVersion(config), HelperPath, directory, fixture.DependencyPath, fixture.ExtensionPath
        })
        {
            start.ArgumentList.Add(argument);
        }
        await RunProcess(start, fixture.Directory);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(99, false)]
    public void PrefersCompatibleSharedFramework(int localMajor, bool useFramework)
    {
        string directory = Path.Combine(RepoLayout.UploadRoot, "hosting", "framework", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "System.Text.Json.dll");
        CreateAssembly(path, "System.Text.Json", localMajor);
        output.WriteLine($"Framework selection fixture: {path}");
        SOSLoadContext context = new(directory);
        Assembly assembly = context.LoadFromAssemblyName(new AssemblyName("System.Text.Json, Version=1.0.0.0"));
        Assert.Same(context, AssemblyLoadContext.GetLoadContext(assembly));
        Assert.Same(typeof(object).Assembly, context.LoadFromAssemblyName(typeof(object).Assembly.GetName()));
        Assert.Equal(useFramework ? Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Text.Json.dll") : path,
            assembly.Location);
    }

    [Fact]
    public void DoesNotFallBackToDefaultContext()
    {
        Assembly assembly = typeof(TestExtension.TestCommand1).Assembly;
        Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(assembly));
        SOSLoadContext context = new(Path.Combine(AppContext.BaseDirectory, "Hosting"));
        Assert.Throws<FileNotFoundException>(() => context.LoadFromAssemblyName(assembly.GetName()));
    }

    [WindowsTheory(SkipTestWithoutData = true)]
    [MemberData(nameof(CdbConfigurations))]
    public async Task CdbExtensionLoadsConflictingDependencyFirst(TestConfig config, int conflictVersion)
    {
        Fixture fixture = CreateFixture(nameof(CdbExtensionLoadsConflictingDependencyFirst), config, conflictVersion);
        string nativeExtension = Path.Combine(RepoLayout.ArtifactsBinNative, "soscohost.dll");
        Assert.True(File.Exists(nativeExtension), $"Native cohost missing: {nativeExtension}");
        string runtimeDirectory = Path.Combine(RepoLayout.DotnetTestRoot, "shared", "Microsoft.NETCore.App", RuntimeVersion(config));
        string dump = SnapshotStore.GetDump(config.Flavor, config.Target, "crash", coreVersion: config.CoreVersion);
        string commandsPath = Path.Combine(fixture.Directory, "commands.txt");
        // A separate native CDB process is essential: the managed dbgeng host initializes SOS itself.
        await File.WriteAllLinesAsync(commandsPath, new[]
        {
            ".unload sos",
            ".echo SOS_COHOST",
            $".load @\"{nativeExtension}\"",
            $"!soscohost.initialize {runtimeDirectory}|{HelperPath}|{fixture.DependencyPath}",
            ".echo SOS_LOAD",
            $".load @\"{ToolPaths.SosPath}\"",
            "!sos runtimes --usecdac false",
            ".echo SOS_BEFORE",
            "!sos clrthreads",
            ".echo SOS_EXTENSION",
            "!sos assemblies",
            ".echo SOS_CHECK",
            "!soscohost.check",
            ".echo SOS_SHUTDOWN",
            "!soscohost.shutdown",
            ".echo SOS_AFTER",
            "!sos clrthreads",
            ".echo SOS_DONE",
            "q"
        });
        ProcessStartInfo start = new(Path.Combine(ToolPaths.DbgEngDirectory, "cdb.exe"));
        foreach (string argument in new[] { "-z", dump, "-cf", commandsPath })
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["DOTNET_DIAGNOSTIC_EXTENSIONS"] = fixture.ExtensionPath;
        string text = await RunProcess(start, fixture.Directory);
        Assert.Contains($"COHOST dependency version={conflictVersion}, HRESULT=00000000",
            Section(text, "SOS_COHOST", "SOS_LOAD"), StringComparison.Ordinal);
        Assert.Contains("ThreadCount:", Section(text, "SOS_BEFORE", "SOS_EXTENSION"), StringComparison.Ordinal);
        Assert.Contains("Test command #4 invoked", Section(text, "SOS_EXTENSION", "SOS_CHECK"), StringComparison.Ordinal);
        Assert.Contains($"COHOST dependency version={conflictVersion}, HRESULT=00000000",
            Section(text, "SOS_CHECK", "SOS_SHUTDOWN"), StringComparison.Ordinal);
        Assert.Contains("COHOST shutdown HRESULT=00000001", Section(text, "SOS_SHUTDOWN", "SOS_AFTER"), StringComparison.Ordinal);
        Assert.Contains("ThreadCount:", Section(text, "SOS_AFTER", "SOS_DONE"), StringComparison.Ordinal);
    }

    private static string Section(string text, string start, string end)
    {
        Match match = Regex.Match(text, $@"(?ms)^{start}\r?$(.*?)^{end}\r?$");
        Assert.True(match.Success, $"Missing CDB section {start} through {end}:\n{text}");
        return match.Groups[1].Value;
    }

    private static string HelperPath => Path.Combine(AppContext.BaseDirectory, "Hosting", "SOS.Cohost.dll");

    private static string RuntimeVersion(TestConfig config) =>
        CoreVersions.RuntimeVersion(config.CoreVersion)
        ?? throw new InvalidOperationException($"No configured runtime for {config.CoreVersion}.");

    private static string GetLayoutDirectory()
    {
        string directory = Path.Combine(RepoLayout.ArtifactsBinNative, "net8.0");
        Assert.True(File.Exists(Path.Combine(directory, "SOS.Hosting.Loader.dll")), $"SOS loader missing from {directory}");
        return directory;
    }

    private sealed record Fixture(string Directory, string DependencyPath, string ExtensionPath);

    private static Fixture CreateFixture(string test, TestConfig config, int version)
    {
        string directory = Path.Combine(RepoLayout.UploadRoot, "hosting", test, CoreVersions.Tfm(config.CoreVersion),
            version.ToString(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string dependencyPath = Path.Combine(directory, "Microsoft.Diagnostics.Runtime.dll");
        CreateAssembly(dependencyPath, "Microsoft.Diagnostics.Runtime", version);
        string extensionDirectory = Path.Combine(directory, "extension");
        Directory.CreateDirectory(extensionDirectory);
        string extensionPath = Path.Combine(extensionDirectory, "TestExtension.dll");
        File.Copy(typeof(TestExtension.TestCommand1).Assembly.Location, extensionPath);
        File.Copy(Path.Combine(GetLayoutDirectory(), "System.Collections.Immutable.dll"),
            Path.Combine(extensionDirectory, "System.Collections.Immutable.dll"));
        return new(directory, dependencyPath, extensionPath);
    }

    private static void CreateAssembly(string path, string name, int major)
    {
        PersistedAssemblyBuilder assembly = new(new AssemblyName(name) { Version = new Version(major, 0, 0, 0) }, typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(name);
        module.DefineType("ConflictingDependency", TypeAttributes.Public).CreateType();
        assembly.Save(path);
    }

    private async Task<string> RunProcess(ProcessStartInfo start, string directory)
    {
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.Environment["DOTNET_ROOT"] = RepoLayout.DotnetTestRoot;
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        start.Environment["_NT_SYMBOL_PATH"] = RepoLayout.SymbolCache;
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Failed to start {start.FileName}.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        string text;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            text = await stdout + await stderr;
            await File.WriteAllTextAsync(Path.Combine(directory, "output.log"), text);
            output.WriteLine(text);
        }
        Assert.True(process.ExitCode == 0, $"{start.FileName} exited with {process.ExitCode}:\n{text}");
        return text;
    }
}
