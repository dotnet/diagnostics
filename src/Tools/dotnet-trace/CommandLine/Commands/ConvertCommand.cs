// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.IO;
using System.CommandLine;
using System.CommandLine.Builder;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class ConvertCommandHandler
    {
        public static int ConvertFile(IConsole console, FileInfo inputFilename, TraceFileFormat format, FileInfo output)
        {
            if ((int)format <= 0)
            {
                Console.Error.WriteLine("--format is required.");
                return ErrorCodes.ArgumentError;
            }

            if (format == TraceFileFormat.NetTrace)
            {
                Console.Error.WriteLine("Cannot convert a nettrace file to nettrace format.");
                return ErrorCodes.ArgumentError;
            }

            if (!inputFilename.Exists)
            {
                Console.Error.WriteLine($"File '{inputFilename}' does not exist.");
                return ErrorCodes.ArgumentError;
            }

            if (output == null)
                output = inputFilename;

            TraceFileFormatConverter.ConvertToFormat(format, inputFilename.FullName, output.FullName);
            return 0;
        }

        public static Command ConvertCommand() =>
            new Command(
                name: "convert",
                description: "Converts traces to alternate formats for use with alternate trace analysis tools. Can only convert from the nettrace format") {
                Handler = System.CommandLine.Invocation.CommandHandler.Create<IConsole, FileInfo, TraceFileFormat, FileInfo>(ConvertFile)
            }.AddOptions(
                new Option[] { 
                    CommonOptions.ConvertFormatOption(), 
                    OutputOption() 
                }
            ).AddArguments(
                new Argument<FileInfo>(name: "input-filename", defaultValue: new FileInfo(CollectCommandHandler.DefaultTraceName)) {
                    Description = $"Input trace file to be converted. Defaults to '{CollectCommandHandler.DefaultTraceName}'."
                }.ExistingOnly());

        public static Option OutputOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: "Output filename. Extension of target format will be added.") {
                Argument = new Argument<FileInfo>(name: "output-filename")
            };
    }
}
