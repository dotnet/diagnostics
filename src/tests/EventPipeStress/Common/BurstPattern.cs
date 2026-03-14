using System;
using System.Diagnostics;
using System.Threading;

namespace Common
{
    // Each pattern sends N events per second, but varies the frequency
    // across that second to simulate various burst patterns.
    public enum BurstPattern : int
    {
        DRIP  = 0, // (default) to send N events per second, sleep 1000/N ms between sends
        BOLUS = 1, // each second send N events then stop
        HEAVY_DRIP = 2, // each second send M bursts of N/M events
        NONE = -1 // no burst pattern
    }

    public static class BurstPatternMethods
    {
        public static BurstPattern ToBurstPattern(this int n)
        {
            return n > 2 || n < -1 ? BurstPattern.NONE : (BurstPattern)n;
        }

        public static BurstPattern ToBurstPattern(this string str)
        {
            if (Int32.TryParse(str, out int result))
                return result.ToBurstPattern();
            else
            {
                return str.ToLowerInvariant() switch
                {
                    "drip" => BurstPattern.DRIP,
                    "bolus" => BurstPattern.BOLUS,
                    "heavy_drip" => BurstPattern.HEAVY_DRIP,
                    "none" => BurstPattern.NONE,
                    _ => BurstPattern.NONE
                };
            }
        }

        public static string ToString(this BurstPattern burstPattern) => burstPattern switch
            {
                BurstPattern.DRIP => "DRIP",
                BurstPattern.BOLUS => "BOLUS",
                BurstPattern.HEAVY_DRIP => "HEAVY_DRIP",
                BurstPattern.NONE => "NONE",
                _ => "UNKOWN"
            };

        public static void DefaultSleepAction(int duration) => Thread.Sleep(duration);
        public static void BusySleepAction(int duration)
        {
            string busyString = "0";
            DateTime start = DateTime.Now;
            while (DateTime.Now.Subtract(start).TotalMilliseconds < duration)
            {
                busyString += "0";
            }
        }

        /// <summary>
        /// Invoke <param name="method"/> <param name="rate"/> times in 1 second using the <param name="pattern"/> provided
        /// </summary>
        public static Func<long> Burst(BurstPattern pattern, int rate, Action method, Action<int> sleepAction = null)
        {
            if (rate == 0)
                throw new ArgumentException("Rate cannot be 0");
            if (sleepAction == null)
                throw new ArgumentException("sleep action cannot be null");

            switch (pattern)
            {
                case BurstPattern.DRIP:
                {
                    int sleepInMs = (int)Math.Floor(1000.0/rate);
                    return () => { method(); sleepAction?.Invoke(sleepInMs); return 1; };
                }
                case BurstPattern.BOLUS:
                {
                    return () => 
                    {
                        Stopwatch sw = new Stopwatch();
                        sw.Start();
                        for (int i = 0; i < rate; i++) { method(); } 
                        sw.Stop();
                        if (sw.Elapsed.TotalSeconds < 1)
                            sleepAction?.Invoke(1000 - (int)Math.Floor((double)sw.ElapsedMilliseconds));
                        return rate;
                    };
                }
                case BurstPattern.HEAVY_DRIP:
                {
                    int nDrips = 4;
                    int nEventsPerDrip = (int)Math.Floor((double)rate / nDrips);
                    int sleepInMs = (int)Math.Floor((1000.0 / rate) / nDrips);
                    return () =>
                    {
                        for (int i = 0; i < nDrips; i++)
                        {
                            for (int j = 0; j < nEventsPerDrip; i++)
                                method();
                            sleepAction?.Invoke(sleepInMs);
                        }
                        return nEventsPerDrip * nDrips;
                    };
                }
                case BurstPattern.NONE:
                {
                    return () => { method(); return 1; };
                }
                default:
                    throw new ArgumentException("Unkown burst pattern");
            }
        }
    }
}