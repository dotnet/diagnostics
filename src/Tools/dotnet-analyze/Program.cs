// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Internal.Utilities;

namespace Microsoft.Diagnostics.Tools.Analyze
{
    [Command(Name = "dotnet-analyze", Description = "Inspect a crash dump using interactive commands")]
    internal class Program
    {
        [Argument(0, "<DIAG_FILES>", Description = "The path to the diagnostic files to analyze.")]
        public IList<string> Files { get; set; }

        public async Task<int> OnExecuteAsync(IConsole console, CommandLineApplication app)
        {
            var cleanupFiles = new List<string>();

            MemoryDump dump = null;
            TraceLog trace = null;

            if (Files == null || Files.Count == 0)
            {
                console.Error.WriteLine("No files were provided!");
                return 1;
            }

            try
            {
                foreach (var file in Files)
                {
                    if (file.EndsWith(".netperf"))
                    {
                        console.WriteLine($"Loading trace: {file} ...");
                        var etlx = TraceLog.CreateFromEventPipeDataFile(file);
                        console.WriteLine($"Convert trace to: {etlx}.");
                        cleanupFiles.Add(etlx);
                        trace = TraceLog.OpenOrConvert(etlx);
                    }
                    else
                    {
                        console.WriteLine($"Loading crash dump: {file} ...");
                        var target = DataTarget.LoadCrashDump(file);
                        // Assume there's only one
                        if (target.ClrVersions.Count > 1)
                        {
                            console.Error.WriteLine("Multiple CLR versions are present!");
                            return 1;
                        }

                        var runtime = target.ClrVersions[0].CreateRuntime();
                        dump = new MemoryDump(target, runtime);
                    }
                }

                if (dump == null && trace == null)
                {
                    console.Error.WriteLine("A dump or trace could not be loaded from the provided files");
                    return 1;
                }
                var session = new AnalysisSession(dump, trace);
                await (new CommandProcessor()).RunAsync(console, session, console.GetCtrlCToken());
                return 0;
            }
            finally
            {
                dump?.Dispose();
                trace?.Dispose();

                foreach (var file in cleanupFiles)
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
            }
        }

        private static int Main(string[] args)
        {
            DebugUtil.WaitForDebuggerIfRequested(ref args);

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
