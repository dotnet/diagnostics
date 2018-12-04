using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Internal.Utilities;

namespace Microsoft.Diagnostics.Tools.Collect
{
    [Command(Name = "dotnet-collect", Description = "Collects Event Traces from .NET processes")]
    internal class Program
    {

        [Option("-c|--config-path <CONFIG_PATH>", Description = "The path of the EventPipe config file to write, must be named [AppName].eventpipeconfig and be in the base directory for a managed app.")]
        public string ConfigPath { get; set; }

        [Option("--etw", Description = "Specify this flag to use ETW to collect events rather than using EventPipe (Windows only).")]
        public bool Etw { get; set; }

        [Required]
        [Option("-p|--process-id <PROCESS_ID>", Description = "Filter to only the process with the specified process ID.")]
        public int ProcessId { get; set; }

        [Option("-o|--output <OUTPUT_DIRECTORY>", Description = "The directory to write the trace to. Defaults to the current working directory.")]
        public string OutputDir { get; set; }

        [Option("--buffer <BUFFER_SIZE_IN_MB>", Description = "The size of the in-memory circular buffer in megabytes.")]
        public int? CircularMB { get; set; }

        [Option("--provider <PROVIDER_SPEC>", Description = "An EventPipe provider to enable. A string in the form '<provider name>:<keywords>:<level>:<parameters>'. Can be specified multiple times to enable multiple providers.")]
        public IList<string> Providers { get; set; }

        [Option("--profile <PROFILE_NAME>", Description = "A collection profile to enable. Use '--list-profiles' to get a list of all available profiles. Can be mixed with '--provider' and specified multiple times.")]
        public IList<string> Profiles { get; set; }

        [Option("--logger <LOGGER_NAME>", Description = "A Microsoft.Extensions.Logging logger to enable. A string in the form '<logger prefix>:<level>'. Can be specified multiple times to enable multiple loggers.")]
        public IList<string> Loggers { get; set; }

        [Option("--keywords-for <PROVIDER_NAME>", Description = "Gets a list of known keywords (if any) for the specified provider.")]
        public string KeywordsForProvider { get; set; }

        [Option("--list-profiles", Description = "Gets a list of predefined collection profiles.")]
        public bool ListProfiles { get; set; }

        [Option("--no-default", Description = "Don't enable the default profile.")]
        public bool NoDefault { get; set; }

        public async Task<int> OnExecuteAsync(IConsole console, CommandLineApplication app)
        {
            if (ListProfiles)
            {
                WriteProfileList(console.Out);
                return 0;
            }

            if (!string.IsNullOrEmpty(KeywordsForProvider))
            {
                return ExecuteKeywordsForAsync(console);
            }

            if(string.IsNullOrEmpty(ConfigPath))
            {
                ConfigPath = ConfigPathDetector.TryDetectConfigPath(ProcessId);
                if(string.IsNullOrEmpty(ConfigPath))
                {
                    console.Error.WriteLine("Couldn't determine the path for the eventpipeconfig file from the process ID. Specify the '--config-path' option to provide it manually.");
                    return 1;
                }
                console.WriteLine($"Detected config file path: {ConfigPath}");
            }

            var config = new CollectionConfiguration()
            {
                ProcessId = ProcessId,
                CircularMB = CircularMB,
                OutputPath = string.IsNullOrEmpty(OutputDir) ? Directory.GetCurrentDirectory() : OutputDir
            };

            if (Profiles != null && Profiles.Count > 0)
            {
                foreach (var profile in Profiles)
                {
                    if (!KnownData.TryGetProfile(profile, out var collectionProfile))
                    {
                        console.Error.WriteLine($"Unknown profile name: '{profile}'. See 'dotnet-collect --list-profiles' to get a list of profiles.");
                        return 1;
                    }
                    config.AddProfile(collectionProfile);
                }
            }

            if (Providers != null && Providers.Count > 0)
            {
                foreach (var provider in Providers)
                {
                    if (!EventSpec.TryParse(provider, out var providerSpec))
                    {
                        console.Error.WriteLine($"Invalid provider specification: '{provider}'. See 'dotnet-collect --help' for more information.");
                        return 1;
                    }
                    config.Providers.Add(providerSpec);
                }
            }

            if (Loggers != null && Loggers.Count > 0)
            {
                foreach (var logger in Loggers)
                {
                    if (!LoggerSpec.TryParse(logger, out var loggerSpec))
                    {
                        console.Error.WriteLine($"Invalid logger specification: '{logger}'. See 'dotnet-collect --help' for more information.");
                        return 1;
                    }
                    config.Loggers.Add(loggerSpec);
                }
            }

            if (!NoDefault)
            {
                // Enable the default profile if nothing is specified
                if (!KnownData.TryGetProfile(CollectionProfile.DefaultProfileName, out var defaultProfile))
                {
                    console.Error.WriteLine("No providers or profiles were specified and there is no default profile available.");
                    return 1;
                }
                config.AddProfile(defaultProfile);
            }

            if (!TryCreateCollector(console, config, out var collector))
            {
                return 1;
            }

            // Write the config file contents
            await collector.StartCollectingAsync();
            console.WriteLine("Tracing has started. Press Ctrl-C to stop.");

            await console.WaitForCtrlCAsync();

            await collector.StopCollectingAsync();
            console.WriteLine($"Tracing stopped. Trace files written to {config.OutputPath}");

            return 0;
        }

        private static void WriteProfileList(TextWriter console)
        {
            var profiles = KnownData.GetAllProfiles();
            var maxNameLength = profiles.Max(p => p.Name.Length);
            console.WriteLine("Available profiles:");
            foreach (var profile in profiles)
            {
                console.WriteLine($"* {profile.Name.PadRight(maxNameLength)}  {profile.Description}");
            }
        }

        private int ExecuteKeywordsForAsync(IConsole console)
        {
            if (KnownData.TryGetProvider(KeywordsForProvider, out var provider))
            {
                console.WriteLine($"Known keywords for {provider.Name} ({provider.Guid}):");
                foreach (var keyword in provider.Keywords.Values)
                {
                    console.WriteLine($"* 0x{keyword.Value:x16} {keyword.Name}");
                }
                return 0;
            }
            else
            {
                console.WriteLine($"There are no known keywords for {KeywordsForProvider}.");
                return 1;
            }
        }

        private bool TryCreateCollector(IConsole console, CollectionConfiguration config, out EventCollector collector)
        {
            collector = null;

            if (Etw)
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    console.Error.WriteLine("Error: ETW-based collection is only supported on Windows.");
                    return false;
                }

                if (!string.IsNullOrEmpty(ConfigPath))
                {
                    console.Error.WriteLine("WARNING: The '-c' option is ignored when using ETW-based collection.");
                }
                collector = new EtwCollector(config);
                return true;
            }
            else
            {
                if (File.Exists(ConfigPath))
                {
                    console.Error.WriteLine("Config file already exists, tracing is already underway by a different consumer.");
                    return false;
                }

                collector = new EventPipeCollector(config, ConfigPath);
                return true;
            }
        }

        private static int Main(string[] args)
        {
            DebugUtil.WaitForDebuggerIfRequested(ref args);

            try
            {
                var app = new CommandLineApplication<Program>();
                app.Conventions.UseDefaultConventions();
                app.ExtendedHelpText = GetExtendedHelp();
                return app.Execute(args);
            }
            catch (PlatformNotSupportedException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            catch (CommandLineException clex)
            {
                Console.Error.WriteLine(clex.Message);
                return 1;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
        }

        private static string GetExtendedHelp()
        {
            using (var writer = new StringWriter())
            {
                writer.WriteLine();
                writer.WriteLine("Profiles");
                writer.WriteLine("  Profiles are sets of well-defined provider configurations that provide useful information.");
                writer.WriteLine();
                WriteProfileList(writer);
                writer.WriteLine();
                writer.WriteLine("Specifying Loggers:");
                writer.WriteLine("  Use one of the following formats to specify a logger in '--logger'");
                writer.WriteLine("    *                                                 - Enable all messages at all levels from all loggers.");
                writer.WriteLine("    *:<level>                                         - Enable messages at the specified '<level>' or higher from all loggers.");
                writer.WriteLine("    <loggerPrefix>                                    - Enable all messages at all levels from all loggers starting with '<loggerPrefix>'.");
                writer.WriteLine("    <loggerPrefix>:<level>                            - Enable messages at the specified '<level>' or higher from all loggers starting with '<loggerPrefix>'.");
                writer.WriteLine();
                writer.WriteLine("  '<loggerPrefix>' is the prefix for a logger to enable. For example 'Microsoft.AspNetCore' to enable all ASP.NET Core loggers.");
                writer.WriteLine("  '<level>' can be one of: Critical, Error, Warning, Informational, Debug, or Trace.");
                writer.WriteLine();
                writer.WriteLine("Specifying Providers:");
                writer.WriteLine("  Use one of the following formats to specify a provider in '--provider'");
                writer.WriteLine("    <providerName>                                    - Enable all events at all levels for the provider.");
                writer.WriteLine("    <providerName>:<keywords>                         - Enable events matching the specified keywords for the specified provider.");
                writer.WriteLine("    <providerName>:<keywords>:<level>                 - Enable events matching the specified keywords, at the specified level for the specified provider.");
                writer.WriteLine("    <providerName>:<keywords>:<level>:<parameters>    - Enable events matching the specified keywords, at the specified level for the specified provider and provide key-value parameters.");
                writer.WriteLine();
                writer.WriteLine("  '<provider>' must be the name of the EventSource.");
                writer.WriteLine("  '<level>' can be one of: Critical (1), Error (2), Warning (3), Informational (4), Verbose (5). Either the name or number can be specified.");
                writer.WriteLine("  '<keywords>' is one of the following:");
                writer.WriteLine("    A '*' character, indicating ALL keywords should be enabled (this can be very costly for some providers!)");
                writer.WriteLine("    A comma-separated list of known keywords for a provider (use 'dotnet collect --keywords-for [providerName]' to get a list of known keywords for a provider)");
                writer.WriteLine("    A 64-bit hexadecimal number, starting with '0x' indicating the keywords to enable");
                writer.WriteLine("  '<parameters>' is an optional list of key-value parameters to provide to the EventPipe provider. The expected values depend on the provider you are enabling.");
                writer.WriteLine("    This should be a list of key-value pairs, in the form: '<key1>=<value1>;<key2>=<value2>;...'. Note that some shells, such as PowerShell, require that you");
                writer.WriteLine("    quote or escape the ';' character.");
                return writer.GetStringBuilder().ToString();
            }
        }
    }
}
