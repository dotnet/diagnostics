// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tools.Trace.DiagnosticProfileHandlers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Internal.Common.Utils;
using Microsoft.Tools.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Rendering;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;


namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class MonitorCommandHandler
    {
        delegate Task<int> MonitorDelegate(
            CancellationToken ct,
            IConsole console,
            // options
            int processId,
            string name,
            uint buffersize,
            // user-defined providers
            string providers,
            // diagnostic profiles
            string profiles
        );

        /// <summary>
        /// Starts a trace and displays the trace's payload live from a process.
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="console"></param>
        /// <param name="processId">The process to collect the trace from.</param>
        /// <param name="name">The name of process to collect the trace from.</param>
        /// <param name="output">The output path for the collected trace data.</param>
        /// <param name="buffersize">Sets the size of the in-memory circular buffer in megabytes.</param>
        /// <param name="providers">A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: 'KnownProviderName[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'</param>
        /// <param name="profile">A named pre-defined set of provider configurations that allows common tracing scenarios to be specified succinctly.</param>
        /// <param name="format">The desired format of the created trace file.</param>
        /// <param name="duration">The duration of trace to be taken. </param>
        private static async Task<int> Monitor(
            CancellationToken ct,
            IConsole console,
            // trace specific options
            int processId,
            string name,
            uint buffersize,
            // user-defined providers
            string providers,
            // diagnostic profiles
            string profiles
        )
        {
            if (!ProcessLauncher.Launcher.HasChildProc)
            {
                // Either processName or processId has to be specified.
                if (name != null)
                {
                    if (processId != 0)
                    {
                        Console.WriteLine("Can only specify either --name or --process-id option.");
                        return ErrorCodes.ArgumentError;
                    }
                    processId = CommandUtils.FindProcessIdWithName(name);
                    if (processId < 0)
                    {
                        return ErrorCodes.ArgumentError;
                    }
                }
                if (processId < 0)
                {
                    Console.Error.WriteLine("Process ID should not be negative.");
                    return ErrorCodes.ArgumentError;
                }
                else if (processId == 0)
                {
                    Console.Error.WriteLine("--process-id is required");
                    return ErrorCodes.ArgumentError;
                }
            }

            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine("Input redirection is not supported with this command.");
                return ErrorCodes.ArgumentError;
            }

            // Build provider string
            var providerCollection = Extensions.ToProviders(providers);
            providerCollection.AddRange(DiagnosticProfileBuilder.GetProfileProviders(profiles));

            if (providerCollection.Count == 0)
            {
                Console.Error.WriteLine("No providers were specified. Use --profiles or --providers option to target at least one event provider to monitor.");
                return ErrorCodes.ArgumentError;
            }

            // Build DiagnosticsClient
            DiagnosticsClient diagnosticsClient;
            if (ProcessLauncher.Launcher.HasChildProc)
            {
                try
                {
                    diagnosticsClient = ReversedDiagnosticsClientBuilder.Build(ProcessLauncher.Launcher, "dotnet-trace", 10);
                }
                catch (TimeoutException)
                {
                    Console.Error.WriteLine("Unable to start tracing session - the target app failed to connect to the diagnostics transport. This may happen if the target application is running .NET Core 3.1 or older versions. Attaching at startup is only available from .NET 5.0 or later.");
                    if (!ProcessLauncher.Launcher.ChildProc.HasExited)
                    {
                        ProcessLauncher.Launcher.ChildProc.Kill();
                    }
                    return ErrorCodes.SessionCreationError;
                }
            }
            else
            {
                diagnosticsClient = new DiagnosticsClient(processId);
            }

            EventPipeSession session = diagnosticsClient.StartEventPipeSession(providerCollection);
            if (ProcessLauncher.Launcher.HasChildProc)
            {
                diagnosticsClient.ResumeRuntime();
            }

            ManualResetEvent shouldExit = new ManualResetEvent(false);
            ct.Register(() => shouldExit.Set());

            Task parserTask = Task.Run(() =>
            {
                try
                {
                    using (EventPipeEventSource source = new EventPipeEventSource(session.EventStream))
                    {

                        foreach (IDiagnosticProfileHandler handler in GetAllHandlers(providerCollection))
                        {
                            handler.RegisterHandler(source);
                        }
                        source.Process();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                }
                finally
                {
                    shouldExit.Set();
                }
            });

            while(!shouldExit.WaitOne(250))
            {
                while (true)
                {
                    if (shouldExit.WaitOne(250))
                    {
                        StopSession(session);
                        return 0;
                    }
                    if (Console.KeyAvailable)
                    {
                        break;
                    }
                }
                ConsoleKey cmd = Console.ReadKey(true).Key;
                if (cmd == ConsoleKey.Q)
                {
                    StopSession(session);
                    break;
                }
            }

            return await Task.FromResult(1);
        }

        /// <summary>
        /// Small wrapper around EventPipeSession.Stop() to catch any Timeout exceptions
        /// A TimeoutException or ServerNotAvailableException may be thrown while stopping the session in the case of:
        ///     1. The app exited before the user stopped the tool.
        ///     2. The app exited after the user stopped the tool, but before the tool sent stop command.
        /// </summary>
        /// <param name="session"></param>
        private static void StopSession(EventPipeSession session)
        {
            try
            {
                session.Stop();
            }
            catch (TimeoutException) { }
            catch (ServerNotAvailableException) { }
        }

        /// <summary>
        /// Generates a List of appropriate IDiagnosticProfileHandlers with the given options from users.
        /// This may throw if the user specified invalid options for any of the diagnostic profile they specified.
        /// </summary>
        /// <param name="providers">List of EventPipeProviders</param>
        /// <returns></returns>
        private static List<IDiagnosticProfileHandler> GetAllHandlers(List<EventPipeProvider> providers)
        {
            List<IDiagnosticProfileHandler> handlers = new List<IDiagnosticProfileHandler>();
            
            foreach (EventPipeProvider provider in providers)
            {
                if (provider.Name.Equals("Microsoft-Windows-DotNETRuntime"))
                {
                    if ((provider.Keywords & (long)ClrTraceEventParser.Keywords.GC) > 0)
                    {
                        handlers.Add(new GcPauseHandler());
                    }
                    if (((provider.Keywords & (long)ClrTraceEventParser.Keywords.Loader) > 0) && (provider.Keywords & (long)ClrTraceEventParser.Keywords.Binder) > 0)
                    {
                        handlers.Add(new LoaderBinderHandler());
                    }
                }
                else
                {
                    handlers.Add(new CustomProviderHandler());
                }
            }
            return handlers;
        }
        public static Option DiagnosticProfileOption() =>
            new Option(
                alias: "--profiles",
                description: "List of diagnostic profiles to enable. Use comma-separated list to specify multiple diagnostic profiles.")
            {
                Argument = new Argument<string>(name: "profiles", getDefaultValue: () => null)
            };

        public static Command MonitorCommand() =>
             new Command(
                name: "monitor",
                description: "Collects a diagnostic trace from a currently running process")
             {
                 // Handler
                 HandlerDescriptor.FromDelegate((MonitorDelegate)Monitor).GetCommandHandler(),
                 // Options for trace session configuration
                 CommonOptions.ProcessIdOption(),
                 CommonOptions.CircularBufferOption(),
                 CommonOptions.NameOption(),
                 // Option for specifying custom providers
                 CommonOptions.ProvidersOption(),
                 // Options for diagnostic profiles
                 DiagnosticProfileOption(),
             };


    }
}