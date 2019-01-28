// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using McMaster.Extensions.CommandLineUtils;
using SOS;
using System;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.SOS
{
    [Command(Name = "dotnet-analyze", Description = "Install and configure SOS")]
    internal class Program
    {
        [Option("--install", Description = "Install and configure SOS.")]
        public bool InstallSOS { get; set; }

        [Option("--uninstall", Description = "Uninstall SOS.")]
        public bool UninstallSOS { get; set; }

        public int OnExecute(IConsole console, CommandLineApplication app)
        {
            if (InstallSOS || UninstallSOS)
            {
                var sosInstaller = new InstallHelper();
                try
                {
                    if (UninstallSOS)
                    {
                        console.WriteLine("Uninstalling SOS from {0}", sosInstaller.InstallLocation);
                        sosInstaller.Uninstall();
                    }
                    else 
                    {
                        console.WriteLine("Installing SOS to {0}", sosInstaller.InstallLocation);
                        sosInstaller.Install();

                        if (sosInstaller.LLDBInitFile != null) {
                            console.WriteLine("Configuring LLDB {0}", sosInstaller.LLDBInitFile);
                            sosInstaller.Configure();
                        }
                    }
                }
                catch (Exception ex)
                {
                    console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }
            return 0;
        }

        private static int Main(string[] args)
        {
            try
            {
                return CommandLineApplication.Execute<Program>(args);
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
        }
    }
}
