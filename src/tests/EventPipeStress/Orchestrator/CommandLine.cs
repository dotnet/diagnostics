using Common;
using System;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Orchestrator
{
    public static class OrchestrateCommandLine
    {
        static public Option<ReaderType> ReaderTypeOption = 
            new Option<ReaderType>(
                alias: "--reader-type",
                getDefaultValue: () => ReaderType.Stream,
                description: "The method to read the stream of events.");

        static public Option<bool> PauseOption = 
            new Option<bool>(
                alias: "--pause",
                getDefaultValue: () => false,
                description: "Should the orchestrator pause before starting each test phase for a debugger to attach?");

        static public Option<bool> RundownOption = 
            new Option<bool>(
                alias: "--rundown",
                getDefaultValue: () => true,
                description: "Should the EventPipe session request rundown events?");

        static private Option<int> _bufferSizeOption = null;
        static public Option<int> BufferSizeOption 
        {
            get
            {
                if (_bufferSizeOption != null)
                    return _bufferSizeOption;

                _bufferSizeOption = new Option<int>(
                                            alias: "--buffer-size",
                                            getDefaultValue: () => 256,
                                            description: "The size of the buffer requested in the EventPipe session");
                _bufferSizeOption.AddValidator(CommandLineOptions.GreaterThanZeroValidator);
                return _bufferSizeOption;
            }
            private set {}
        }

        static private Option<int> _slowReaderOption = null;
        static public Option<int> SlowReaderOption 
        {
            get
            {
                if (_slowReaderOption != null)
                    return _slowReaderOption;

                _slowReaderOption = new Option<int>(
                                            alias: "--slow-reader",
                                            getDefaultValue: () => 0,
                                            description: "<Only valid for EventPipeEventSource reader> Delay every read by this many milliseconds.");
                _slowReaderOption.AddValidator(CommandLineOptions.GreaterThanOrEqualZeroValidator);
                return _slowReaderOption;
            }
            private set {}
        }

        static private Option<int> _coresOption = null;
        static public Option<int> CoresOption 
        {
            get
            {
                if (_coresOption != null)
                    return _coresOption;

                _coresOption = new Option<int>(
                                        alias: "--cores",
                                        getDefaultValue: () => Environment.ProcessorCount,
                                        description: "The number of logical cores to restrict the writing process to.");
                _coresOption.AddValidator(CoreValueMustBeFeasibleValidator);
                return _coresOption;
            }
            private set {}
        }

        static private Option<int> _iterationsOption = null;
        static public Option<int> IterationsOption 
        {
            get
            {
                if (_iterationsOption != null)
                    return _iterationsOption;

                _iterationsOption = new Option<int>(
                                        alias: "--iterations",
                                        getDefaultValue: () => 1,
                                        description: "The number of times to run the test.");
                _iterationsOption.AddValidator(CommandLineOptions.GreaterThanZeroValidator);
                return _iterationsOption;
            }
            private set {}
        }

        static public ValidateSymbol<OptionResult> CoreValueMustBeFeasibleValidator = (OptionResult result) =>
        {
            int val = result.GetValueOrDefault<int>();
            if (val < 1 || val > Environment.ProcessorCount)
                return $"Core count must be between 1 and {Environment.ProcessorCount}";
            return null;
        };
    }
}