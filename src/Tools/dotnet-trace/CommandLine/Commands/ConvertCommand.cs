// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.IO;
using System.CommandLine;
using System.CommandLine.Builder;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class ConvertCommandHandler
    {
        public static int ConvertFile(IConsole console, FileInfo inputFilename, TraceFileFormat format, FileInfo output)
        {
            try
            {
                if (format == TraceFileFormat.netperf)
                    throw new ArgumentException("Cannot convert to netperf format.");
                
                if (!inputFilename.Exists)
                    throw new FileNotFoundException($"File '{inputFilename}' does not exist.");

                TraceFileFormatConverter.ConvertToFormat(format, inputFilename.FullName, output.FullName);

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                return 1;
            }
        }

        public static Command ConvertCommand() =>
            new Command(
                name: "convert",
                description: "Converts traces to alternate formats for use with alternate trace analysis tools. Can only convert from the netperf format.",
                argument: new Argument<FileInfo>(defaultValue: new FileInfo(CollectCommandHandler.DefaultTraceName)) { 
                    Name = "input-filename",
                    Description = $"Input trace file to be converted.  Defaults to '{CollectCommandHandler.DefaultTraceName}'.",
                    Arity = ArgumentArity.ExactlyOne 
                },
                symbols: new Option[] {
                    CommonOptions.FormatOption(),
                    OutputOption()
                },
                handler: System.CommandLine.Invocation.CommandHandler.Create<IConsole, FileInfo, TraceFileFormat, FileInfo>(ConvertFile),
                isHidden: false
            );

        public static Option OutputOption() =>
            new Option(
                aliases: new [] { "-o", "--output" },
                description: "Output filename. Extension of target format will be added.",
                argument: new Argument<FileInfo>(defaultValue: new FileInfo(CollectCommandHandler.DefaultTraceName)) { Name = "output-filename", Arity = ArgumentArity.ExactlyOne },
                isHidden: false
            );
    }
}
