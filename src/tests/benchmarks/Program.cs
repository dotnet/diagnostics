using System;
using System.Reflection;
using System.Threading;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Jobs;

namespace CounterBenchmarks
{
    [SimpleJob(RuntimeMoniker.HostProcess)]
    [RPlotExporter, RankColumn]
    public class CounterBenchmarks
    {
        private MethodInfo getGenerationSize;
        private MethodInfo getPercentTimeInGC;
        private MethodInfo getExceptionCount;
        private MethodInfo getAssemblyCount;

        // These are pre-allocated objects for passing as parameters private reflection methods to avoid allocation overhead in the actual benchmark.
        private object[] gen0SizeParam = new object[] { 0 };
        private object[] gen1SizeParam = new object[] { 1 };
        private object[] gen2SizeParam = new object[] { 2 };
        private object[] gen3SizeParam = new object[] { 3 };

        [Params(10000)]
        public int N;

        [GlobalSetup]
        public void Setup()
        {
            // Use reflection to get all the internal API we use inside the runtime to emit counters.
            Assembly SPC = typeof(System.Diagnostics.Tracing.EventSource).Assembly;
            if (SPC == null)
            {
                Environment.FailFast("Failed to get System.Private.CoreLib assembly.");
            }

            Type gcType = SPC.GetType("System.GC");
            if (gcType == null)
            {
                Environment.FailFast("Failed to get System.GC type.");
            }
            getGenerationSize = gcType.GetMethod("GetGenerationSize", BindingFlags.Static | BindingFlags.NonPublic);
            getPercentTimeInGC = gcType.GetMethod("GetLastGCPercentTimeInGC", BindingFlags.Static | BindingFlags.NonPublic);

            Type exceptionType = SPC.GetType("System.Exception");
            if (exceptionType == null)
            {
                Environment.FailFast("Failed to get System.Environment Type.");
            }
            getExceptionCount = exceptionType.GetMethod("GetExceptionCount", BindingFlags.Static | BindingFlags.NonPublic);

            Type assemblyType = SPC.GetType("System.Reflection.Assembly");
            if (assemblyType == null)
            {
                Environment.FailFast("Failed to get System.Reflection.Assembly Type.");
            }
            getAssemblyCount = assemblyType.GetMethod("GetAssemblyCount", BindingFlags.Static | BindingFlags.NonPublic);
        }

        [Benchmark]
        public int Gen0Count() => GC.CollectionCount(0);

        [Benchmark]
        public int Gen1Count() => GC.CollectionCount(1);

        [Benchmark]
        public int Gen2Count() => GC.CollectionCount(2);

        [Benchmark]
        public object Gen0Size() => getGenerationSize.Invoke(null, gen0SizeParam);

        [Benchmark]
        public object Gen1Size() => getGenerationSize.Invoke(null, gen1SizeParam);

        [Benchmark]
        public object Gen2Size() => getGenerationSize.Invoke(null, gen2SizeParam);

        [Benchmark]
        public object LOHSize() => getGenerationSize.Invoke(null, gen3SizeParam);

        [Benchmark]
        public object TimeInGC() => getPercentTimeInGC.Invoke(null, null);

        [Benchmark]
        public object AssemblyCount() => getAssemblyCount.Invoke(null, null);

        [Benchmark]
        public object ExceptionCount() => getExceptionCount.Invoke(null, null);

        [Benchmark]
        public long Workingset() => Environment.WorkingSet;

        [Benchmark]
        public long ThreadPoolCount() => ThreadPool.ThreadCount;

        [Benchmark]
        public long LockContentionCount() => Monitor.LockContentionCount;

        [Benchmark]
        public long PendingItemCount() => ThreadPool.PendingWorkItemCount;

        [Benchmark]
        public long CompletedItemCount() => ThreadPool.CompletedWorkItemCount;

        [Benchmark]
        public long TotalAllocatedBytes() => GC.GetTotalAllocatedBytes();

        [Benchmark]
        public long ActiveTimerCount() => Timer.ActiveCount;

    }

    public class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<CounterBenchmarks>();
        }
    }
}