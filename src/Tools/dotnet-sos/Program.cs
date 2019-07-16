// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using SOS;
using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.SOS
{
    public class Program
    {
        public static Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                .AddCommand(InstallCommand())
                .AddCommand(UninstallCommand())
                .UseDefaults()
                .Build();

            return parser.InvokeAsync(args);
        }

        private static Command InstallCommand() =>
            new Command(
                "install", 
                "Installs SOS and configures LLDB to load it on startup.", 
                handler: CommandHandler.Create<IConsole>((console) => InvokeAsync(console, install: true)));

        private static Command UninstallCommand() =>
            new Command(
                "uninstall",
                "Uninstalls SOS and reverts any configuration changes to LLDB.",
                handler: CommandHandler.Create<IConsole>((console) => InvokeAsync(console, install: false)));

        private static Task<int> InvokeAsync(IConsole console, bool install)
        {
            try
            {
                var sosInstaller = new InstallHelper((message) => console.Out.WriteLine(message));
                if (install) {
                    sosInstaller.Install();
                }
                else {
                    sosInstaller.Uninstall();
                }
            }
            catch (SOSInstallerException ex)
            {
                console.Error.WriteLine(ex.Message);
                return Task.FromResult(1);
            }
            return Task.FromResult(0);
        }
    }
}
