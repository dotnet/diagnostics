// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "runtimes", Help = "Lists the runtimes in the target or change the default runtime.")]
    public class RuntimesCommand : CommandBase
    {
        public IRuntimeService RuntimeService { get; set; }

        public IContextService ContextService { get; set; }

        public ITarget Target { get; set; }

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
                        WriteLine("Switched to {0} runtime successfully", name);
                        return;
                    }
                }
                WriteLineError("The {0} runtime is not loaded", name);
            }
            else
            {
                // Display the current runtime star ("*") only if there is more than one runtime
                bool displayStar = RuntimeService.EnumerateRuntimes().Count() > 1;

                foreach (IRuntime runtime in RuntimeService.EnumerateRuntimes())
                {
                    string current = displayStar ? (runtime == ContextService.GetCurrentRuntime() ? "*" : " ") : "";
                    Write(current);
                    Write(runtime.ToString());
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
                }
            }
        }
    }

    public static class Utilities
    {
        public static string ToHex(this ImmutableArray<byte> array) => string.Concat(array.Select((b) => b.ToString("x2")));
    }
}
