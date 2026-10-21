// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

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
        Assembly assembly = ToolPaths.Layout.LoadSosAssembly();
        Console.Error.WriteLine($"SOS entry point: {assembly.Location}");
        MethodInfo initialize = assembly.GetType("SOS.Extensions.HostServices", throwOnError: true)!
            .GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static, [typeof(string)])
            ?? throw new MissingMethodException("SOS.Extensions.HostServices.Initialize(string) was not found.");
        return initialize.CreateDelegate<Func<string, int>>();
    }
}
