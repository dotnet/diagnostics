// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "runtimes", Help = "Lists the runtimes in the target or changes the default runtime.")]
    public class RuntimesCommand : CommandBase
    {
        [ServiceImport]
        public IRuntimeService RuntimeService { get; set; }

        [ServiceImport]
        public IContextService ContextService { get; set; }

        [ServiceImport]
        public ITarget Target { get; set; }

        [Argument(Help = "Switch to the runtime by id.")]
        public int? Id { get; set; }

        [Option(Name = "--netfx", Aliases = new string[] { "-netfx", "-f" }, Help = "Switches to the desktop .NET Framework if exists.")]
        public bool NetFx { get; set; }

        [Option(Name = "--netcore", Aliases = new string[] { "-netcore", "-c" }, Help = "Switches to the .NET Core or .NET 5+ runtime if exists.")]
        public bool NetCore { get; set; }

        public override void Invoke()
        {
            if (NetFx && NetCore)
            {
                throw new DiagnosticsException("Cannot specify both -netfx and -netcore options");
            }
            if (NetFx || NetCore)
            {
                string name = NetFx ? "desktop .NET Framework" : ".NET Core";
                foreach (IRuntime runtime in RuntimeService.EnumerateRuntimes())
                {
                    if (NetFx && runtime.RuntimeType == RuntimeType.Desktop ||
                        NetCore && runtime.RuntimeType == RuntimeType.NetCore)
                    {
                        ContextService.SetCurrentRuntime(runtime.Id);
                        WriteLine($"Switched to {name} runtime successfully");
                        return;
                    }
                }
                WriteLineError($"The {name} runtime is not loaded");
            }
            else if (Id.HasValue)
            {
                ContextService.SetCurrentRuntime(Id.Value);
                WriteLine($"Switched to runtime #{Id.Value} successfully");
            }
            else
            {
                // Display the current runtime star ("*") only if there is more than one runtime and it is the current one
                bool displayStar = RuntimeService.EnumerateRuntimes().Count() > 1;
                IRuntime currentRuntime = ContextService.GetCurrentRuntime();

                foreach (IRuntime runtime in RuntimeService.EnumerateRuntimes())
                {
                    string current = displayStar ? (runtime == currentRuntime ? "*" : " ") : "";
                    Write(current);
                    WriteLine(runtime.ToString());
                    ClrInfo clrInfo = runtime.Services.GetService<ClrInfo>();
                    if (clrInfo is not null)
                    {
                        WriteLine("    Libraries:");
                        foreach (DebugLibraryInfo library in clrInfo.DebuggingLibraries)
                        {
                            string index = library.IndexBuildId.IsDefaultOrEmpty ? $"{library.IndexTimeStamp:X8} {library.IndexFileSize:X8}" : library.IndexBuildId.ToHex();
                            WriteLine($"        {library.Kind} {library.FileName} {library.Platform} {library.TargetArchitecture} {library.ArchivedUnder} {index}");
                        }
                    }
                    this.DisplayResources(runtime.RuntimeModule, all: false, indent: "    ");
                    this.DisplayRuntimeExports(runtime.RuntimeModule, error: true, indent: "    ");
                }
            }
        }
    }
}
