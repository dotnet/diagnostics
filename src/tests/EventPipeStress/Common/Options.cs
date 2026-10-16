using System;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Common
{
    public static class CommandLineOptions
    {
        static public Action<OptionResult> GreaterThanZeroValidator = (OptionResult result) =>
        {
            if (result.GetValueOrDefault<int>() <= 0)
                result.AddError($"{result.Option.Name} must be greater than or equal to 0");
        };

        static public Action<OptionResult> GreaterThanOrEqualZeroValidator = (OptionResult result) =>
        {
            if (result.GetValueOrDefault<int>() < 0)
                result.AddError($"{result.Option.Name} must be greater than 0");
        };

        static public Action<OptionResult> MustBeNegOneOrPositiveValidator = (OptionResult result) =>
        {
            int val = result.GetValueOrDefault<int>();
            if (val < -1 || val == 0)
                result.AddError($"{result.Option.Name} must be -1 or greater than 0");
        };

        static private Option<int> _eventSizeOption = null;
        static public Option<int> EventSizeOption 
        {
            get
            {
                if (_eventSizeOption != null)
                    return _eventSizeOption;

                _eventSizeOption = new Option<int>("--event-size")
                {
                    DefaultValueFactory = _ => 100,
                    Description = "The size of the event payload.  The payload is a string, so the actual size will be eventSize * sizeof(char) where sizeof(char) is 2 Bytes due to Unicode in C#."
                };
                _eventSizeOption.Validators.Add(GreaterThanZeroValidator);
                return _eventSizeOption;
            }
            private set {}
        }

        static private Option<int> _eventRateOption = null;
        static public Option<int> EventRateOption
        {
            get
            {
                if (_eventRateOption != null)
                    return _eventRateOption;

                _eventRateOption = new Option<int>("--event-rate")
                {
                    DefaultValueFactory = _ => -1,
                    Description = "The rate of events in events/sec.  -1 means 'as fast as possible'."
                };
                _eventRateOption.Validators.Add(MustBeNegOneOrPositiveValidator);
                return _eventRateOption;
            }
            private set {}
        }

        static public Option<BurstPattern> BurstPatternOption =
            new Option<BurstPattern>("--burst-pattern")
            {
                DefaultValueFactory = _ => BurstPattern.NONE,
                Description = "The burst pattern to send events in."
            };

        static private Option<int> _durationOption = null;
        static public Option<int> DurationOption
        {
            get
            {
                if (_durationOption != null)
                    return _durationOption;

                _durationOption = new Option<int>("--duration")
                {
                    DefaultValueFactory = _ => 60,
                    Description = "The number of seconds to send events for."
                };
                _durationOption.Validators.Add(GreaterThanZeroValidator);
                return _durationOption;
            }
            private set {}
        }


        static private Option<int> _threadsOption = null;
        static public Option<int> ThreadsOption
        {
            get
            {
                if (_threadsOption != null)
                    return _threadsOption;

                _threadsOption = new Option<int>("--threads")
                {
                    DefaultValueFactory = _ => 1,
                    Description = "The number of threads writing events."
                };
                _threadsOption.Validators.Add(GreaterThanZeroValidator);
                return _threadsOption;
            }
            private set {}
        }


        static private Option<int> _eventCountOption = null;
        static public Option<int> EventCountOption
        {
            get
            {
                if (_eventCountOption != null)
                    return _eventCountOption;

                _eventCountOption = new Option<int>("--event-count")
                {
                    DefaultValueFactory = _ => -1,
                    Description = "The total number of events to write per thread.  -1 means no limit"
                };
                _eventCountOption.Validators.Add(MustBeNegOneOrPositiveValidator);
                return _eventCountOption;
            }
            private set {}
        }
    }
}