// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.IO;
using Graphs;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    internal static class ConvertCommandHandler
    {
        public static int ConvertFile(FileInfo input, string output, bool verbose)
        {
            if (!input.Exists)
            {
                Console.Error.WriteLine($"File '{input.FullName}' does not exist.");
                return -1;
            }

            output = string.IsNullOrEmpty(output)
                    ? Path.ChangeExtension(input.FullName, "gcdump")
                    : output;

            FileInfo outputFileInfo = new(output);

            if (outputFileInfo.Exists)
            {
                outputFileInfo.Delete();
            }

            if (string.IsNullOrEmpty(outputFileInfo.Extension) || outputFileInfo.Extension != ".gcdump")
            {
                outputFileInfo = new FileInfo(outputFileInfo.FullName + ".gcdump");
            }

            Console.Out.WriteLine($"Writing gcdump to '{outputFileInfo.FullName}'...");

            DotNetHeapInfo heapInfo = new();
            TextWriter log = verbose ? Console.Out : TextWriter.Null;

            MemoryGraph memoryGraph = new(50_000);

            if (!EventPipeDotNetHeapDumper.DumpFromEventPipeFile(input.FullName, memoryGraph, log, heapInfo))
            {
                return -1;
            }

            memoryGraph.AllowReading();
            GCHeapDump.WriteMemoryGraph(memoryGraph, outputFileInfo.FullName, "dotnet-gcdump");

            return 0;
        }

        public static System.CommandLine.Command ConvertCommand() =>
            new(
                name: "convert",
                description: "Converts nettrace file into .gcdump file handled by analysis tools. Can only convert from the nettrace format.")
            {
                // Handler
                System.CommandLine.Invocation.CommandHandler.Create<FileInfo, string, bool>(ConvertFile),
                // Arguments and Options
                InputPathArgument(),
                OutputPathOption(),
                VerboseOption()
            };

        private static Argument<FileInfo> InputPathArgument() =>
            new Argument<FileInfo>("input")
            {
                Description = "Input trace file to be converted.",
                Arity = new ArgumentArity(0, 1)
            }.ExistingOnly();

        private static Option<string> OutputPathOption() =>
            new(
                aliases: new[] { "-o", "--output" },
                description: $@"The path where converted gcdump should be written. Defaults to '<input>.gcdump'")
            {
                Argument = new Argument<string>(name: "output", getDefaultValue: () => string.Empty)
            };

        private static Option<bool> VerboseOption() =>
            new(
                aliases: new[] { "-v", "--verbose" },
                description: "Output the log while converting the gcdump.")
            {
                Argument = new Argument<bool>(name: "verbose", getDefaultValue: () => false)
            };
    }
}
