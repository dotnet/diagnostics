using Common;
using System;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Orchestrator
{
    public static class OrchestrateCommandLine
    {
        static public Option<ReaderType> ReaderTypeOption = 
            new Option<ReaderType>("--reader-type")
            {
                DefaultValueFactory = _ => ReaderType.Stream,
                Description = "The method to read the stream of events."
            };

        static public Option<bool> PauseOption = 
            new Option<bool>("--pause")
            {
                DefaultValueFactory = _ => false,
                Description = "Should the orchestrator pause before starting each test phase for a debugger to attach?"
            };

        static public Option<bool> RundownOption = 
            new Option<bool>("--rundown")
            {
                DefaultValueFactory = _ => true,
                Description = "Should the EventPipe session request rundown events?"
            };

        static private Option<int> _bufferSizeOption = null;
        static public Option<int> BufferSizeOption 
        {
            get
            {
                if (_bufferSizeOption != null)
                    return _bufferSizeOption;

                _bufferSizeOption = new Option<int>("--buffer-size")
                {
                    DefaultValueFactory = _ => 256,
                    Description = "The size of the buffer requested in the EventPipe session"
                };
                _bufferSizeOption.Validators.Add(CommandLineOptions.GreaterThanZeroValidator);
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

                _slowReaderOption = new Option<int>("--slow-reader")
                {
                    DefaultValueFactory = _ => 0,
                    Description = "<Only valid for EventPipeEventSource reader> Delay every read by this many milliseconds."
                };
                _slowReaderOption.Validators.Add(CommandLineOptions.GreaterThanOrEqualZeroValidator);
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

                _coresOption = new Option<int>("--cores")
                {
                    DefaultValueFactory = _ => Environment.ProcessorCount,
                    Description = "The number of logical cores to restrict the writing process to."
                };
                _coresOption.Validators.Add(CoreValueMustBeFeasibleValidator);
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

                _iterationsOption = new Option<int>("--iterations")
                {
                    DefaultValueFactory = _ => 1,
                    Description = "The number of times to run the test."
                };
                _iterationsOption.Validators.Add(CommandLineOptions.GreaterThanZeroValidator);
                return _iterationsOption;
            }
            private set {}
        }

        static public Action<OptionResult> CoreValueMustBeFeasibleValidator = (OptionResult result) =>
        {
            int val = result.GetValueOrDefault<int>();
            if (val < 1 || val > Environment.ProcessorCount)
                result.AddError($"Core count must be between 1 and {Environment.ProcessorCount}");
        };
    }
}