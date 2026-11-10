// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.TestHelpers;
using SOS.Hosting.Loader;
using Xunit;

public class SOSHostRuntimeTests
{
    private readonly ITestOutputHelper _output;

    public SOSHostRuntimeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> HostRuntimeConfigurations =>
        SOSTestHelpers.GetNetCoreConfigurations()
            .Select(arguments => (TestConfiguration)arguments[0])
            .Where(config => !config.PublishSingleFile)
            .SelectMany(config => new[] { new object[] { config, 1 }, new object[] { config, 99 } });

    [Theory]
    [InlineData("clean")]
    [InlineData("conflict")]
    [InlineData("missing")]
    [InlineData("cleanup")]
    public Task FrameworkHostIsolatesDependenciesAndInitialization(string scenario) =>
        RunFrameworkHostTest(scenario);

    [Theory]
    [InlineData("native-update-failure")]
    [InlineData("native-registration-failure")]
    [InlineData("native-no-target")]
    [InlineData("native-success")]
    public Task NativeHostInitializationHandlesTargetStatus(string scenario)
    {
        if (OS.Kind != OSKind.Windows)
        {
            Assert.Skip("Native SOS hosting test is Windows-only");
        }

        TestConfiguration config = SOSTestHelpers.GetNetCoreConfigurations()
            .Select(arguments => (TestConfiguration)arguments[0])
            .First();
        return RunFrameworkHostTest(scenario, config.SOSPath());
    }

    private static async Task RunFrameworkHostTest(string scenario, string sosPath = null)
    {
        if (OS.Kind != OSKind.Windows)
        {
            Assert.Skip(".NET Framework hosting is Windows-only");
        }

        string hostDirectory = Path.Combine(AppContext.BaseDirectory, "FrameworkHostTest");
        string directory = Path.Combine(Path.GetTempPath(), "SOS.FrameworkHost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ProcessStartInfo start = new(Path.Combine(hostDirectory, "SOS.FrameworkHost.TestHost.exe"))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.Combine(hostDirectory, "SOS.FrameworkHost.dll"));
            start.ArgumentList.Add(directory);
            start.ArgumentList.Add(scenario);
            if (sosPath != null)
            {
                start.ArgumentList.Add(sosPath);
            }
            using Process process = Process.Start(start);
            Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(1));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }
            Assert.True(process.ExitCode == 0, await output + Environment.NewLine + await error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(HostRuntimeConfigurations))]
    public async Task IsolatesPreviouslyLoadedDependencies(TestConfiguration config, int conflictVersion)
    {
        string directory = GetLayoutDirectory(config);
        string dependencyPath = CreateConflictingDependency(config, conflictVersion);
        using TestRunner.OutputHelper output = TestRunner.ConfigureLogging(config, _output, $"SOS.Hosting.{conflictVersion}");
        string helperPath = Path.Combine(AppContext.BaseDirectory, "Hosting", "SOS.Cohost.dll");
        string extensionPath = CreateExtension(directory, dependencyPath);
        using ProcessRunner runner = new ProcessRunner(config.CliPath,
            $"--fx-version {config.RuntimeFrameworkVersion} \"{helperPath}\" \"{directory}\" \"{dependencyPath}\" \"{extensionPath}\"")
            .WithLog(output)
            .WithTimeout(TimeSpan.FromMinutes(1))
            .WithExpectedExitCode(0);
        runner.Start();
        await runner.WaitForExit();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(99, false)]
    public void PrefersCompatibleSharedFramework(int localMajor, bool useFramework)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Hosting", "framework", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "System.Text.Json.dll");
        CreateAssembly(path, "System.Text.Json", localMajor);
        _output.WriteLine($"Framework selection fixture: {path}");
        SOSLoadContext context = new(directory);
        Assembly assembly = context.LoadFromAssemblyName(new AssemblyName("System.Text.Json, Version=1.0.0.0"));
        Assert.Same(context, AssemblyLoadContext.GetLoadContext(assembly));
        Assert.Same(typeof(object).Assembly, context.LoadFromAssemblyName(typeof(object).Assembly.GetName()));
        Assert.Equal(useFramework ? Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location), "System.Text.Json.dll") : path,
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

    [Theory]
    [MemberData(nameof(HostRuntimeConfigurations))]
    public async Task CdbExtensionLoadsConflictingDependencyFirst(TestConfiguration config, int conflictVersion)
    {
        Assert.SkipWhen(OS.Kind != OSKind.Windows, "Requires the Windows debugger.");
        string dependencyPath = CreateConflictingDependency(config, conflictVersion);
        Dictionary<string, string> settings = new(config.AllSettings)
        {
            ["DotNetDiagnosticExtensions"] = CreateExtension(GetLayoutDirectory(config), dependencyPath)
        };
        config = new TestConfiguration(settings);
        using SOSRunner runner = await SOSRunner.StartDebugger(new SOSRunner.TestInformation
        {
            TestConfiguration = config,
            OutputHelper = _output,
            TestName = $"SOS.Hosting.Cdb.{conflictVersion}",
            DebuggeeName = "DivZero"
        }, SOSRunner.DebuggerAction.Live);
        string nativeExtension = Path.Combine(Path.GetDirectoryName(config.SOSPath()), "soscohost.dll");
        string runtimeDirectory = Path.Combine(config.DotNetRoot, "shared", "Microsoft.NETCore.App", config.RuntimeFrameworkVersion);
        string helperPath = Path.Combine(AppContext.BaseDirectory, "Hosting", "SOS.Cohost.dll");
        await runner.RunCommands(new[]
        {
            $".load \"{nativeExtension}\"",
            $"!soscohost.initialize {runtimeDirectory}|{helperPath}|{dependencyPath}"
        });
        runner.VerifyOutput($"COHOST dependency version={conflictVersion}", match: true);
        await runner.ContinueExecution();
        await runner.LoadSosExtension();
        Assert.True(await runner.RunSosCommand("clrmodules"));
        runner.VerifyOutput("DivZero", match: true);
        Assert.True(await runner.RunSosCommand("assemblies"));
        runner.VerifyOutput("Test command #4 invoked", match: true);
        await runner.RunCommands(new[] { "!soscohost.check" });
        runner.VerifyOutput($"COHOST dependency version={conflictVersion}", match: true);
        Assert.True(await runner.RunSosCommand("clrmodules"));
        runner.VerifyOutput("DivZero", match: true);
        await runner.RunCommands(new[] { "!soscohost.shutdown" });
        runner.VerifyOutput("COHOST shutdown HRESULT=00000001", match: true);
        Assert.True(await runner.RunSosCommand("clrmodules"));
        runner.VerifyOutput("DivZero", match: true);
        await runner.QuitDebugger();
    }

    private static string GetLayoutDirectory(TestConfiguration config)
    {
        string directory = Path.Combine(Path.GetDirectoryName(config.SOSPath()), config.GetValue("NetCoreAppMinTargetFramework"));
        Assert.True(File.Exists(Path.Combine(directory, "SOS.Hosting.Loader.dll")), $"SOS loader missing from {directory}");
        return directory;
    }

    private static string CreateConflictingDependency(TestConfiguration config, int version)
    {
        string directory = Path.Combine(config.LogDirPath, "hosting", config.RuntimeFrameworkVersion, version.ToString());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Microsoft.Diagnostics.Runtime.dll");
        CreateAssembly(path, "Microsoft.Diagnostics.Runtime", version);
        return path;
    }

    private static string CreateExtension(string layoutDirectory, string dependencyPath)
    {
        string directory = Path.Combine(Path.GetDirectoryName(dependencyPath), "extension");
        Directory.CreateDirectory(directory);
        string extensionPath = Path.Combine(directory, "TestExtension.dll");
        File.Copy(typeof(TestExtension.TestCommand1).Assembly.Location, extensionPath, overwrite: true);
        File.Copy(Path.Combine(layoutDirectory, "System.Collections.Immutable.dll"),
            Path.Combine(directory, "System.Collections.Immutable.dll"), overwrite: true);
        return extensionPath;
    }

    private static void CreateAssembly(string path, string name, int major)
    {
        PersistedAssemblyBuilder assembly = new(new AssemblyName(name) { Version = new Version(major, 0, 0, 0) }, typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(name);
        module.DefineType("ConflictingDependency", TypeAttributes.Public).CreateType();
        assembly.Save(path);
    }
}
