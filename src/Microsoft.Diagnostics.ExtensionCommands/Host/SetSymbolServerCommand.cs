// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(
        Name = "setsymbolserver",
        Aliases = new string[] { "SetSymbolServer" },
        Help = "Enable and set symbol server support for symbols and module download",
        Flags = CommandFlags.Global)]
    [Command(
        Name = "loadsymbols",
        DefaultOptions = "--loadsymbols",
        Help = "Load symbols for all modules",
        Flags = CommandFlags.Global)]
    public class SetSymbolServerCommand : CommandBase
    {
        public ISymbolService SymbolService { get; set; }

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
            if (LoadSymbols)
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
    }
}
