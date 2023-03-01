// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Tools.Common;
using SOS;

namespace Microsoft.Diagnostics.Tools.SOS
{
    public class Program
    {
        public static Task<int> Main(string[] args)
        {
            Parser parser = new CommandLineBuilder()
                .AddCommand(InstallCommand())
                .AddCommand(UninstallCommand())
                .UseDefaults()
                .Build();

            return parser.InvokeAsync(args);
        }

        private static Command InstallCommand() =>
            new Command(
                name: "install",
                description: "Installs SOS and configures LLDB to load it on startup.")
            {
                // Handler
                CommandHandler.Create<IConsole, Architecture?>((console, architecture) => InvokeAsync(console, architecture, install: true)),
                // Options
                ArchitectureOption()
            };

        private static Option ArchitectureOption() =>
            new Option(
                aliases: new[] { "-a", "--arch", "--architecture" },
                description: "The processor architecture to install.")
            {
                Argument = new Argument<Architecture>(name: "architecture")
            };

        private static Command UninstallCommand() =>
            new Command(
                name: "uninstall",
                description: "Uninstalls SOS and reverts any configuration changes to LLDB.")
            {
                Handler = CommandHandler.Create<IConsole>((console) => InvokeAsync(console, architecture: null, install: false))
            };

        private static Task<int> InvokeAsync(IConsole console, Architecture? architecture, bool install)
        {
            try
            {
                var sosInstaller = new InstallHelper((message) => console.Out.WriteLine(message), architecture);
                if (install)
                {
                    sosInstaller.Install();
                }
                else
                {
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
