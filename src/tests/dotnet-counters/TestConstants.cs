// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace DotnetCounters.UnitTests
{
    public static class TestConstants
    {
        public const string TestCounter = "TestCounter";
        public const string TestCounterName = TestCounter + " (dollars / 1 sec)";
        public const string TestHistogram = "TestHistogram";
        public const string TestHistogramName = TestHistogram + " (feet)";
        public const string PercentileKey = "Percentile";
        public const string TagKey = "TestTag";
        public const string TagValue = "5";
        public const string TestMeterName = "TestMeter";

        public const int ProviderIndex = 1;
        public const int CounterNameIndex = 2;
        public const int CounterTypeIndex = 3;
        public const int ValueIndex = 4;
    }
}
