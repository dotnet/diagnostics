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
    [Command(Name = "runtimes", Help = "List the runtimes in the target or change the default runtime.")]
    public class RuntimesCommand : CommandBase
    {
        public IRuntimeService RuntimeService { get; set; }

        public IContextService ContextService { get; set; }

        public ITarget Target { get; set; }

        [Option(Name = "--netfx", Aliases = new string[] { "-netfx", "-f" }, Help = "Switches to the desktop .NET Framework if exists.")]
        public bool NetFx { get; set; }

        [Option(Name = "--netcore", Aliases = new string[] { "-netcore", "-c" }, Help = "Switches to the .NET Core runtime if exists.")]
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
                        NetCore && runtime.RuntimeType  == RuntimeType.NetCore)
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
                        unsafe
                        {
                            if (clrInfo.SingleFileRuntimeInfo.HasValue)
                            {
                                RuntimeInfo runtimeInfo = clrInfo.SingleFileRuntimeInfo.Value;
                                WriteLine("    Signature:   {0}", Encoding.ASCII.GetString(runtimeInfo.Signature, RuntimeInfo.SignatureValueLength - 1));
                                WriteLine("    Version:     {0}", runtimeInfo.Version);
                                if (Target.OperatingSystem == OSPlatform.Windows)
                                {
                                    WriteLine("    Runtime:     {0}", GetWindowsIndex(runtimeInfo.RuntimeModuleIndex));
                                    WriteLine("    DBI:         {0}", GetWindowsIndex(runtimeInfo.DbiModuleIndex));
                                    WriteLine("    DAC:         {0}", GetWindowsIndex(runtimeInfo.DacModuleIndex));
                                }
                                else 
                                {
                                    WriteLine("    Runtime:     {0}",  GetUnixIndex(runtimeInfo.RuntimeModuleIndex));
                                    WriteLine("    DBI:         {0}",  GetUnixIndex(runtimeInfo.DbiModuleIndex));
                                    WriteLine("    DAC:         {0}",  GetUnixIndex(runtimeInfo.DacModuleIndex));
                                }
                            }
                        }
                    }
                }
            }
        }

        private unsafe string GetWindowsIndex(byte* index)
        {
            uint timeStamp = BitConverter.ToUInt32(new ReadOnlySpan<byte>(index + sizeof(byte), sizeof(uint)).ToArray(), 0);
            uint fileSize = BitConverter.ToUInt32(new ReadOnlySpan<byte>(index + sizeof(byte) + sizeof(uint), sizeof(uint)).ToArray(), 0);
            return string.Format("TimeStamp {0:X8} FileSize {1:X8}", timeStamp, fileSize);
        }

        private unsafe string GetUnixIndex(byte* index)
        {
            var buildId = new ReadOnlySpan<byte>(index + sizeof(byte), index[0]).ToArray().ToImmutableArray();
            return string.Format("BuildId {0}", ToHex(buildId));
        }

        private string ToHex(ImmutableArray<byte> array) => string.Concat(array.Select((b) => b.ToString("x2")));
    }
}
