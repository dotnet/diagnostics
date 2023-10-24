// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "setsymbolserver", Aliases = new string[] { "SetSymbolServer" }, Help = "Enables and sets symbol server support for symbols and module download.")]
    [Command(Name = "loadsymbols", DefaultOptions = "--loadsymbols", Help = "Loads symbols for all modules.")]
    public class SetSymbolServerCommand : CommandBase
    {
        [ServiceImport]
        public ISymbolService SymbolService { get; set; }

        [ServiceImport(Optional = true)]
        public IModuleService ModuleService { get; set; }

        [Option(Name = "--ms", Aliases = new string[] { "-ms" }, Help = "Use the public Microsoft symbol server.")]
        public bool MicrosoftSymbolServer { get; set; }

        [Option(Name = "--mi", Aliases = new string[] { "-mi" }, Help = "Use the internal symweb symbol server.")]
        public bool InternalSymbolServer { get; set; }

        [Option(Name = "--disable", Aliases = new string[] { "-disable" }, Help = "Clear or disable symbol download support.")]
        public bool Disable { get; set; }

        [Option(Name = "--reset", Aliases = new string[] { "-reset" }, Help = "Reset the HTTP symbol servers clearing any cached failures.")]
        public bool Reset { get; set; }

        [Option(Name = "--cache", Aliases = new string[] { "-cache" }, Help = "Specify a symbol cache directory.")]
        public string Cache { get; set; }

        [Option(Name = "--directory", Aliases = new string[] { "-directory" }, Help = "Specify a directory to search for symbols.")]
        public string Directory { get; set; }

        [Option(Name = "--pat", Aliases = new string[] { "-pat" }, Help = "Access token to the authenticated server.")]
        public string AccessToken { get; set; }

        [Option(Name = "--timeout", Aliases = new string[] { "-timeout" }, Help = "Specify the symbol server timeout in minutes.")]
        public int? Timeout { get; set; }

        [Option(Name = "--retrycount", Aliases = new string[] { "-retrycount" }, Help = "Specify the symbol server timeout retry count.")]
        public int? RetryCount { get; set; }

        [Option(Name = "--loadsymbols", Aliases = new string[] { "-loadsymbols" }, Help = "Attempt to load native symbols for all modules.")]
        public bool LoadSymbols { get; set; }

        [Argument(Name = "url", Help = "Symbol server URL.")]
        public string SymbolServerUrl { get; set; }

        public override void Invoke()
        {
            if (MicrosoftSymbolServer && InternalSymbolServer)
            {
                throw new DiagnosticsException("Cannot have both -ms and -mi options");
            }
            if ((MicrosoftSymbolServer || InternalSymbolServer) && !string.IsNullOrEmpty(SymbolServerUrl))
            {
                throw new DiagnosticsException("Cannot have -ms or -mi option and a symbol server path");
            }
            if (Disable)
            {
                SymbolService.DisableSymbolStore();
            }
            if (Reset)
            {
                SymbolService.Reset();
            }
            if (MicrosoftSymbolServer || InternalSymbolServer || !string.IsNullOrEmpty(SymbolServerUrl))
            {
                if (string.IsNullOrEmpty(Cache))
                {
                    Cache = SymbolService.DefaultSymbolCache;
                }
                SymbolService.AddSymbolServer(MicrosoftSymbolServer, InternalSymbolServer, SymbolServerUrl, AccessToken, Timeout, RetryCount);
            }
            if (!string.IsNullOrEmpty(Cache))
            {
                SymbolService.AddCachePath(Cache);
            }
            if (!string.IsNullOrEmpty(Directory))
            {
                SymbolService.AddDirectoryPath(Directory);
            }
            if (LoadSymbols && ModuleService is not null)
            {
                foreach (IModule module in ModuleService.EnumerateModules())
                {
                    if (!module.IsManaged)
                    {
                        Write($"Downloading symbol file for {module.FileName}");
                        string downloadedModulePath = module.LoadSymbols();
                        WriteLine(" {0}", downloadedModulePath != null ? "SUCCEEDED" : "FAILED");
                    }
                }
            }
            else
            {
                Write(SymbolService.ToString());
            }
        }

        [HelpInvoke]
        public static string GetDetailedHelp(IHost host)
        {
            switch (host.HostType)
            {
                case HostType.DbgEng:
                    return s_detailedHelpTextDbgEng;
                case HostType.Lldb:
                    return s_detailedHelpTextLLDB;
                case HostType.DotnetDump:
                    return s_detailedHelpTextDotNetDump;
            }
            return null;
        }

        private const string s_detailedHelpTextDbgEng =
        @"
This commands enables symbol server support for portable PDBs for managed assemblies and 
.NET Core native modules files (like the DAC) in SOS. If the .sympath is set, the symbol 
server supported is automatically set and this command isn't necessary.
";

        private const string s_detailedHelpTextLLDB =
        @"
This commands enables symbol server support in SOS. The portable PDBs for managed assemblies
and .NET Core native symbol and module (like the DAC) files are downloaded.

To enable downloading symbols from the Microsoft symbol server:

    (lldb) setsymbolserver -ms

This command may take some time without any output while it attempts to download the symbol files. 

To disable downloading or clear the current SOS symbol settings allowing new symbol paths to be set:

    (lldb) setsymbolserver -disable

To add a directory to search for symbols:

    (lldb) setsymbolserver -directory /home/mikem/symbols

This command can be used so the module/symbol file structure does not have to match the machine 
file structure that the core dump was generated.

To clear the default cache run ""rm -r $HOME/.dotnet/symbolcache"" in a command shell.

If you receive an error like the one below on a core dump, you need to set the .NET Core
runtime with the ""sethostruntime"" command. Type ""soshelp sethostruntime"" for more details.

    (lldb) setsymbolserver -ms
    Error: Fail to initialize CoreCLR 80004005
    SetSymbolServer -ms  failed

The ""-loadsymbols"" option and the ""loadsymbol"" command alias attempts to download the native .NET
Core symbol files. It is only useful for live sessions and not core dumps. This command needs to 
be run before the lldb ""bt"" (stack trace) or the ""clrstack -f"" (dumps both managed and native
stack frames).

    (lldb) loadsymbols
    (lldb) bt
";

        private const string s_detailedHelpTextDotNetDump =
        @"
This commands enables symbol server support in SOS. The portable PDBs for managed assemblies
and .NET Core native module (like the DAC) files are downloaded.

To enable downloading symbols from the Microsoft symbol server:

    > setsymbolserver -ms

This command may take some time without any output while it attempts to download the symbol files. 

To disable downloading or clear the current SOS symbol settings allowing new symbol paths to be set:

    > setsymbolserver -disable

To add a directory to search for symbols:

    > setsymbolserver -directory /home/mikem/symbols

This command can be used so the module/symbol file structure does not have to match the machine 
file structure that the core dump was generated.

To clear the default cache run ""rm -r $HOME/.dotnet/symbolcache"" in a command shell.
";
    }
}
