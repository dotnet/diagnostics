// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;
using System.Linq;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class ConvertCommandHandler
    {
        // The first 8 bytes of a nettrace file are the ASCII string "Nettrace"
        private static readonly byte[] NetTraceHeader = [0x4E, 0x65, 0x74, 0x74, 0x72, 0x61, 0x63, 0x65];

        public static int ConvertFile(IConsole console, FileInfo inputFilename, TraceFileFormat format, FileInfo output)
        {
            if (!Enum.IsDefined(format))
            {
                console.Error.WriteLine($"Please specify a valid option for the --format. Valid options are: {string.Join(", ", Enum.GetNames<TraceFileFormat>())}.");
                return ErrorCodes.ArgumentError;
            }

            if (!ValidateNetTraceHeader(console, inputFilename.FullName))
            {
                return ErrorCodes.ArgumentError;
            }

            string outputFilename = TraceFileFormatConverter.GetConvertedFilename(inputFilename.FullName, output?.FullName, format);

            if (format != TraceFileFormat.NetTrace)
            {
                TraceFileFormatConverter.ConvertToFormat(console, format, inputFilename.FullName, outputFilename);
                return 0;
            }

            return CopyNetTrace(console, inputFilename.FullName, outputFilename);

            static bool ValidateNetTraceHeader(IConsole console, string filename)
            {
                try
                {
                    using FileStream fs = new(filename, FileMode.Open, FileAccess.Read);
                    Span<byte> header = stackalloc byte[NetTraceHeader.Length];
                    Span<byte> readBuffer = header;
                    int bytesRead = 0;
                    while (readBuffer.Length > 0 && (bytesRead = fs.Read(readBuffer)) > 0)
                    {
                        readBuffer = readBuffer.Slice(bytesRead);
                    }

                    if (readBuffer.Length != 0 || !header.SequenceEqual(NetTraceHeader))
                    {
                        console.Error.WriteLine($"'{filename}' is not a valid nettrace file.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    console.Error.WriteLine($"Error reading '{filename}': {ex.Message}");
                    return false;
                }

                return true;
            }

            static int CopyNetTrace(IConsole console, string inputfile, string outputfile)
            {
                if (inputfile == outputfile)
                {
                    console.Error.WriteLine("Input and output filenames are the same. Skipping copy.");
                    return 0;
                }

                console.Out.WriteLine($"Copying nettrace to:\t{outputfile}");
                try
                {
                    File.Copy(inputfile, outputfile);
                }
                catch (Exception ex)
                {
                    console.Error.WriteLine($"Error copying nettrace to {outputfile}: {ex.Message}");
                    return ErrorCodes.UnknownError;
                }

                return 0;
            }
        }

        public static Command ConvertCommand() =>
            new(
                name: "convert",
                description: "Converts traces to alternate formats for use with alternate trace analysis tools. Can only convert from the nettrace format")
            {
                // Handler
                System.CommandLine.Invocation.CommandHandler.Create<IConsole, FileInfo, TraceFileFormat, FileInfo>(ConvertFile),
                // Arguments and Options
                InputFileArgument(),
                CommonOptions.ConvertFormatOption(),
                OutputOption(),
            };

        private static Argument InputFileArgument() =>
            new Argument<FileInfo>(name: "input-filename", getDefaultValue: () => new FileInfo(CollectCommandHandler.DefaultTraceName))
            {
                Description = $"Input trace file to be converted. Defaults to '{CollectCommandHandler.DefaultTraceName}'."
            }.ExistingOnly();

        private static Option OutputOption() =>
            new(
                aliases: new[] { "-o", "--output" },
                description: "Output filename. Extension of target format will be added.")
            {
                Argument = new Argument<FileInfo>(name: "output-filename")
            };
    }
}
