// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using McMaster.Extensions.CommandLineUtils;
using SOS;
using System;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.SOS
{
    [Command(Name = "dotnet-sos", Description = "Install and configure SOS")]
    internal class Program
    {
        [Option("--install", Description = "Install and configure SOS.")]
        public bool InstallSOS { get; set; }

        [Option("--uninstall", Description = "Uninstall SOS.")]
        public bool UninstallSOS { get; set; }

        [Option("--source", Description = "SOS binaries source path.")]
        public string SOSSourcePath { get; set; }

        public int OnExecute(IConsole console, CommandLineApplication app)
        {
            if (InstallSOS || UninstallSOS)
            {
                var sosInstaller = new InstallHelper((message) => console.WriteLine(message));
                if (SOSSourcePath != null)
                {
                    sosInstaller.SOSSourcePath = SOSSourcePath;
                }
                try
                {
                    if (UninstallSOS)
                    {
                        sosInstaller.Uninstall();
                    }
                    else 
                    {
                        sosInstaller.Install();
                    }
                }
                catch (Exception ex) when (ex is ArgumentException || ex is PlatformNotSupportedException || ex is InvalidOperationException)
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
