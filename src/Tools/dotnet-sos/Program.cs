// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Runtime.InteropServices;
using SOS;

namespace Microsoft.Diagnostics.Tools.SOS
{
    public class Program
    {
        public static int Main(string[] args)
        {
            RootCommand rootCommand = new()
            {
                InstallCommand(),
                UninstallCommand()
            };

            return rootCommand.Parse(args).Invoke();
        }

        private static Command InstallCommand()
        {
            Command installCommand = new(
                name: "install",
                description: "Installs SOS and configures LLDB to load it on startup.")
            {
                ArchitectureOption
            };

            installCommand.SetAction(parseResult => Invoke(
                parseResult.Configuration.Output,
                parseResult.Configuration.Error,
                architecture: parseResult.GetValue(ArchitectureOption),
                install: true));

            return installCommand;
        }

        private static readonly Option<Architecture?> ArchitectureOption =
            new("--architecture", "-a", "--arch")
            {
                Description = "The processor architecture to install."
            };

        private static Command UninstallCommand()
        {
            Command uninstallCommand = new(
                name: "uninstall",
                description: "Uninstalls SOS and reverts any configuration changes to LLDB.");

            uninstallCommand.SetAction(parseResult => Invoke(
                parseResult.Configuration.Output,
                parseResult.Configuration.Error,
                architecture: null,
                install: false));

            return uninstallCommand;
        }

        private static int Invoke(TextWriter stdOut, TextWriter stdError, Architecture? architecture, bool install)
        {
            try
            {
                InstallHelper sosInstaller = new((message) => stdOut.WriteLine(message), architecture);
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
                stdError.WriteLine(ex.Message);
                return 1;
            }
            return 0;
        }
    }
}
