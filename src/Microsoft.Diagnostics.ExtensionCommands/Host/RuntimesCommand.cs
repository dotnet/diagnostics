// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using System.Linq;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "runtimes", Help = "List the runtimes in the target or change the default runtime.")]
    public class RuntimesCommand : CommandBase
    {
        public IRuntimeService RuntimeService { get; set; }

        public IContextService ContextService { get; set; }

        [Option(Name = "-netfx", Help = "Switches to the desktop .NET Framework if exists.")]
        public bool NetFx { get; set; }

        [Option(Name = "-netcore", Help = "Switches to the .NET Core runtime if exists.")]
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
                }
            }
        }
    }
}
