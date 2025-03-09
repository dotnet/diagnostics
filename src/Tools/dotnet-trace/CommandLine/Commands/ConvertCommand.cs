// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Internal.Common;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class ConvertCommandHandler
    {
        // The first 8 bytes of a nettrace file are the ASCII string "Nettrace"
        private static readonly byte[] NetTraceHeader = [0x4E, 0x65, 0x74, 0x74, 0x72, 0x61, 0x63, 0x65];

        public static int ConvertFile(TextWriter stdOut, TextWriter stdError, FileInfo inputFilename, TraceFileFormat format, FileInfo output)
        {
            if (!Enum.IsDefined(format))
            {
                stdError.WriteLine($"Please specify a valid option for the --format. Valid options are: {string.Join(", ", Enum.GetNames<TraceFileFormat>())}.");
                return ErrorCodes.ArgumentError;
            }

            if (!ValidateNetTraceHeader(stdError, inputFilename.FullName))
            {
                return ErrorCodes.ArgumentError;
            }

            string outputFilename = TraceFileFormatConverter.GetConvertedFilename(inputFilename.FullName, output?.FullName, format);

            if (format != TraceFileFormat.NetTrace)
            {
                TraceFileFormatConverter.ConvertToFormat(stdOut, stdError, format, inputFilename.FullName, outputFilename);
                return 0;
            }

            return CopyNetTrace(stdOut, stdError, inputFilename.FullName, outputFilename);

            static bool ValidateNetTraceHeader(TextWriter stdError, string filename)
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
                        stdError.WriteLine($"'{filename}' is not a valid nettrace file.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    stdError.WriteLine($"Error reading '{filename}': {ex.Message}");
                    return false;
                }

                return true;
            }

            static int CopyNetTrace(TextWriter stdOut, TextWriter stdError, string inputfile, string outputfile)
            {
                if (inputfile == outputfile)
                {
                    stdError.WriteLine("Input and output filenames are the same. Skipping copy.");
                    return 0;
                }

                stdOut.WriteLine($"Copying nettrace to:\t{outputfile}");
                try
                {
                    File.Copy(inputfile, outputfile);
                }
                catch (Exception ex)
                {
                    stdError.WriteLine($"Error copying nettrace to {outputfile}: {ex.Message}");
                    return ErrorCodes.UnknownError;
                }

                return 0;
            }
        }

        public static Command ConvertCommand()
        {
            Command convertCommand = new(
                name: "convert",
                description: "Converts traces to alternate formats for use with alternate trace analysis tools. Can only convert from the nettrace format")
            {
                // Arguments and Options
                InputFileArgument,
                CommonOptions.ConvertFormatOption,
                OutputOption,
            };

            convertCommand.SetAction((parseResult, ct) => Task.FromResult(ConvertFile(
                stdOut: parseResult.Configuration.Output,
                stdError: parseResult.Configuration.Error,
                inputFilename: parseResult.GetValue(InputFileArgument),
                format: parseResult.GetValue(CommonOptions.ConvertFormatOption),
                output: parseResult.GetValue(OutputOption
            ))));

            return convertCommand;
        }

        private static readonly Argument<FileInfo> InputFileArgument =
            new Argument<FileInfo>(name: "input-filename")
            {
                Description = $"Input trace file to be converted. Defaults to '{CollectCommandHandler.DefaultTraceName}'.",
                DefaultValueFactory = _ => new FileInfo(CollectCommandHandler.DefaultTraceName),
            }.AcceptExistingOnly();

        private static readonly Option<FileInfo> OutputOption =
            new("--output", "-o")
            {
                Description = "Output filename. Extension of target format will be added.",
            };
    }
}
