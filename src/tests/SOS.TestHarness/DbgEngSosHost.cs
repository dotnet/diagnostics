// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace SOS.TestHarness;

internal static class DbgEngSosHost
{
    private static readonly Lazy<Func<string, int>> s_initialize = new(ResolveInitialize);

    public static void Initialize()
    {
        // DbgEng is already inside a managed process: connect SOS to that CoreCLR instead of starting another runtime.
        int hr = s_initialize.Value(ToolPaths.SosPath);
        if (hr != 0)
        {
            throw new InvalidOperationException($"HostServices.Initialize('{ToolPaths.SosPath}') failed: 0x{hr:x8}.");
        }
    }

    private static Func<string, int> ResolveInitialize()
    {
        Assembly assembly = LoadSosAssembly(ToolPaths.Layout);
        Console.Error.WriteLine($"SOS entry point: {assembly.Location}");
        MethodInfo initialize = assembly.GetType("SOS.Extensions.HostServices", throwOnError: true)!
            .GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static, [typeof(string)])
            ?? throw new MissingMethodException("SOS.Extensions.HostServices.Initialize(string) was not found.");
        return initialize.CreateDelegate<Func<string, int>>();
    }

    internal static Assembly LoadSosAssembly(IToolLayout layout)
    {
        string assemblyPath = layout.SosAssemblyPath;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (Assembly assembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (!assembly.IsDynamic && string.Equals(assemblyPath, assembly.Location, comparison))
            {
                return assembly;
            }
        }

        return new SosLoadContext(Path.GetDirectoryName(assemblyPath)!).LoadFromAssemblyPath(assemblyPath);
    }

    // Keep the packaged product closure separate from the build-output assemblies referenced by the harness.
    internal sealed class SosLoadContext(string directory) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly HashSet<string> _frameworkAssemblies = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not null && _frameworkAssemblies.Contains(assemblyName.Name))
            {
                return null;
            }

            string path = ToolLayout.RequireFile(Path.Combine(directory, assemblyName.Name + ".dll"));
            Console.Error.WriteLine($"Packaged SOS assembly: {path}");
            return LoadFromAssemblyPath(path);
        }
    }
}
