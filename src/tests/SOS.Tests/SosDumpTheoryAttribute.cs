// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using SOS.TestHarness;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: CollectionBehavior(ParallelAlgorithm = ParallelAlgorithm.Conservative)]

namespace SOS.Tests;

/// <summary>Groups single-stop dump rows without changing live rows or deferred-enumeration theories.</summary>
[XunitTestCaseDiscoverer(typeof(SosDumpTheoryDiscoverer))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class SosDumpTheoryAttribute : TheoryAttribute
{
    public SosDumpTheoryAttribute(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
    }

    public string? StopName { get; set; }
    public string? ForTarget { get; set; }

    internal string GetStop(TestConfig config, object?[] arguments) =>
        StopName is not null && (ForTarget is null || ForTarget == config.Target)
            ? StopName
            : arguments.Length == 2 && arguments[1] is string stop
                ? stop
                : TargetCatalog.Get(config.Target).DefaultStopName;

    internal static string CollectionName(TestConfig config, string stop) =>
        "SOS dump: " + JsonSerializer.Serialize(new {
            config.Target,
            config.Host,
            config.Flavor,
            config.GcType,
            config.DumpKind,
            config.CoreVersion,
            config.Dac,
            config.HostRuntime,
            Stop = stop,
        });
}

public class SosDumpTheoryDiscoverer : TheoryDiscoverer
{
    protected override ValueTask<IReadOnlyCollection<IXunitTestCase>> CreateTestCasesForDataRow(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        ITheoryAttribute theoryAttribute,
        ITheoryDataRow dataRow,
        object?[] testMethodArguments)
    {
        if (testMethodArguments.FirstOrDefault() is TestConfig { IsLive: false } config &&
            theoryAttribute is SosDumpTheoryAttribute attribute &&
            testMethod.TestClass.TestCollection.CollectionDefinition is null)
        {
            string stop = attribute.GetStop(config, testMethodArguments);
            TargetCatalog.Get(config.Target).Stop(stop);
            XunitTestCollection collection = new(
                testMethod.TestClass.TestCollection.TestAssembly,
                typeof(DumpCollectionDefinition),
                testMethod.TestClass.TestCollection.DisableParallelization,
                SosDumpTheoryAttribute.CollectionName(config, stop));
            XunitTestClass testClass = new(testMethod.TestClass.Class, collection);
            testMethod = new XunitTestMethod(testClass, testMethod.Method, testMethodArguments);
        }

        return base.CreateTestCasesForDataRow(
            discoveryOptions, testMethod, theoryAttribute, dataRow, testMethodArguments);
    }
}

[CollectionDefinition]
public sealed class DumpCollectionDefinition : ICollectionFixture<DumpCollectionFixture>
{
}
