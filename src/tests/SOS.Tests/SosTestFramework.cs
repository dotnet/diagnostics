// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using SOS.TestHarness;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: TestFramework(typeof(SOS.Tests.SosTestFramework))]

namespace SOS.Tests;

/// <summary>Aligns session capacity with execution options before invoking the standard xUnit executor.</summary>
public sealed class SosTestFramework : XunitTestFramework
{
    protected override ITestFrameworkExecutor CreateExecutor(Assembly assembly) =>
        new Executor(new XunitTestAssembly(assembly, null, assembly.GetName().Version));

    internal static int ConfigureExecutionOptions(ITestFrameworkExecutionOptions options)
    {
        bool serial = options.DisableParallelization() == true;
        int capacity = serial ? 1 : options.MaxParallelThreads() ?? HostSlotPool.DefaultCapacity;
        if (capacity == 0)
        {
            capacity = Environment.ProcessorCount;
        }
        if (capacity < 0)
        {
            capacity = HostSlotPool.DefaultCapacity;
            Console.Error.WriteLine($"SOS.Tests requires bounded parallelism; using {capacity} threads instead of unlimited.");
        }
        if (!serial && options.ParallelAlgorithm() == ParallelAlgorithm.Aggressive)
        {
            Console.Error.WriteLine("SOS.Tests requires conservative collection scheduling; overriding aggressive parallelism.");
        }

        options.SetMaxParallelThreads(capacity);
        options.SetParallelAlgorithm(ParallelAlgorithm.Conservative);
        return capacity;
    }

    private sealed class Executor(IXunitTestAssembly assembly) : XunitTestFrameworkExecutor(assembly)
    {
        public override ValueTask RunTestCases(
            IReadOnlyCollection<IXunitTestCase> testCases,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions,
            CancellationToken cancellationToken)
        {
            HostSlotPool.ConfigureShared(ConfigureExecutionOptions(executionOptions));
            return base.RunTestCases(testCases, executionMessageSink, executionOptions, cancellationToken);
        }
    }
}
