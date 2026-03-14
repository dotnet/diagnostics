// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "sosstatus", Help = "Displays internal status.")]
    [Command(Name = "sosflush", DefaultOptions = "--reset", Help = "Resets the internal cached state.")]
    public class StatusCommand : CommandBase
    {
        [ServiceImport]
        public IHost Host { get; set; }

        [ServiceImport]
        public IServiceManager ServiceManager { get; set; }

        [ServiceImport]
        public ISymbolService SymbolService { get; set; }

        [ServiceImport]
        public IContextService ContextService { get; set; }

        [Option(Name = "--reset", Aliases = new[] { "-reset" }, Help = "Resets the internal cached state.")]
        public bool Reset { get; set; }

        public override void Invoke()
        {
            if (Reset)
            {
                foreach (ITarget target in Host.EnumerateTargets())
                {
                    target.Flush();
                }
                WriteLine("Internal cached state reset");
            }
            else
            {
                IRuntime currentRuntime = ContextService.GetCurrentRuntime();
                foreach (ITarget target in Host.EnumerateTargets())
                {
                    WriteLine(target.ToString());

                    IRuntimeService runtimeService = target.Services.GetService<IRuntimeService>();

                    // Display the current runtime star ("*") only if there is more than one runtime
                    bool displayStar = runtimeService.EnumerateRuntimes().Count() > 1;

                    foreach (IRuntime runtime in runtimeService.EnumerateRuntimes())
                    {
                        string current = displayStar ? (runtime == currentRuntime ? "*" : " ") : "";
                        WriteLine($"    {current}{runtime}");

                        string indent = new(' ', 8);
                        this.DisplayResources(runtime.RuntimeModule, all: false, indent);
                        this.DisplayRuntimeExports(runtime.RuntimeModule, error: true, indent);
                    }
                }
                this.DisplaySpecialInfo();
                this.DisplaySettingService();
                Write(SymbolService.ToString());

                List<Assembly> extensions = new(ServiceManager.ExtensionsLoaded);
                extensions.Insert(0, Assembly.GetExecutingAssembly());

                WriteLine("Extensions loaded:");
                foreach (Assembly extension in extensions)
                {
                    string path = extension.Location;
                    FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(path);
                    WriteLine($"-> {versionInfo.ProductVersion} {path}");
                }
                WriteLine($"Host runtime {RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}");
                long memoryUsage = GC.GetTotalMemory(forceFullCollection: true);
                WriteLine($"GC memory usage for managed SOS components: {memoryUsage:##,#} bytes");
            }
        }
    }
}
