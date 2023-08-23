// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Information about the CLR Runtime's ThreadPool.
    /// </summary>
    public sealed class ClrThreadPool : IClrThreadPool
    {
        private readonly ClrRuntime _runtime;
        private readonly IClrThreadPoolHelper _helpers;
        private readonly ulong _nativeLogAddress;
        private readonly uint _nativeLogStart;
        private readonly uint _nativeLogSize;

        /// <summary>
        /// Used to track whether we successfully initialized this object to prevent throw/catch.
        /// </summary>
        internal bool Initialized { get; } = true;

        /// <summary>
        /// Whether this runtime is using the Portable threadpool or not.
        /// </summary>
        public bool Portable { get; }

        /// <summary>
        /// The current CPU utilization of the ThreadPool (a number between 0 and 100).
        /// </summary>
        public int CpuUtilization { get; }

        /// <summary>
        /// The minimum number of worker threads allowed for the ThreadPool.
        /// </summary>
        public int MinThreads { get; }

        /// <summary>
        /// The maximum number of worker threads allowed for the ThreadPool.
        /// </summary>
        public int MaxThreads { get; }

        /// <summary>
        /// The number of idle worker threads.
        /// </summary>
        public int IdleWorkerThreads { get; }

        /// <summary>
        /// The number of active worker threads.
        /// </summary>
        public int ActiveWorkerThreads { get; }

        public int TotalCompletionPorts { get; }
        public int FreeCompletionPorts { get; }
        public int MaxFreeCompletionPorts { get; }
        public int CompletionPortCurrentLimit { get; }
        public int MinCompletionPorts { get; }
        public int MaxCompletionPorts { get; }

        /// <summary>
        /// The number of retired worker threads.
        /// </summary>
        public int RetiredWorkerThreads { get; }

        private readonly ClrDataAddress _firstLegacyWorkRequest;
        private readonly ClrDataAddress _asyncTimerFunction;

        internal ClrThreadPool(ClrRuntime runtime, IClrThreadPoolHelper helpers)
        {
            _runtime = runtime;
            _helpers = helpers;

            bool hasLegacyData = _helpers.GetLegacyThreadPoolData(out ThreadPoolData tpData, out bool mustBePortable);

            ClrObject threadPool = GetPortableThreadPool(mustBePortable);
            if (!threadPool.IsNull && threadPool.IsValid)
            {
                Portable = true;
                CpuUtilization = threadPool.ReadField<int>("_cpuUtilization");
                MinThreads = threadPool.ReadField<ushort>("_minThreads");
                MaxThreads = threadPool.ReadField<ushort>("_maxThreads");

                ClrValueType counts = threadPool.ReadValueTypeField("_separated").ReadValueTypeField("counts").ReadValueTypeField("_data");
                ulong dataValue = counts.ReadField<ulong>("m_value");

                int processingWorkCount = (ushort)(dataValue & 0xffff);
                int existingThreadCount = (ushort)((dataValue >> 16) & 0xffff);

                IdleWorkerThreads = existingThreadCount - processingWorkCount;
                ActiveWorkerThreads = processingWorkCount;

                RetiredWorkerThreads = 0;
            }
            else if (hasLegacyData)
            {
                CpuUtilization = tpData.CpuUtilization;
                MinThreads = tpData.MinLimitTotalWorkerThreads;
                MaxThreads = tpData.MaxLimitTotalWorkerThreads;
                IdleWorkerThreads = tpData.NumIdleWorkerThreads;
                ActiveWorkerThreads = tpData.NumWorkingWorkerThreads;
                RetiredWorkerThreads = tpData.NumRetiredWorkerThreads;

                TotalCompletionPorts = tpData.NumCPThreads;
                FreeCompletionPorts = tpData.NumFreeCPThreads;
                MaxFreeCompletionPorts = tpData.MaxFreeCPThreads;
                CompletionPortCurrentLimit = tpData.CurrentLimitTotalCPThreads;
                MaxCompletionPorts = tpData.MaxLimitTotalCPThreads;
                MinCompletionPorts = tpData.MinLimitTotalCPThreads;

                _nativeLogAddress = tpData.HillClimbingLog;
                _nativeLogStart = tpData.HillClimbingLogFirstIndex;
                _nativeLogSize = tpData.HillClimbingLogSize;

                _firstLegacyWorkRequest = tpData.FirstUnmanagedWorkRequest;
                _asyncTimerFunction = tpData.AsyncTimerCallbackCompletionFPtr;
            }
            else
            {
                Initialized = false;
            }
        }

        /// <summary>
        /// Enumerates LegacyThreadPoolWorkRequests.  We only have this for Desktop CLR.
        /// </summary>
        /// <returns>An enumeration of work requests, or an empty enumeration of the runtime
        /// does not have them.</returns>
        public IEnumerable<LegacyThreadPoolWorkRequest> EnumerateLegacyWorkRequests()
        {
            ulong curr = _firstLegacyWorkRequest;
            while (curr != 0 && _helpers.GetLegacyWorkRequestData(curr, out WorkRequestData workRequestData))
            {
                yield return new LegacyThreadPoolWorkRequest()
                {
                    Context = workRequestData.Context,
                    Function = workRequestData.Function,
                    IsAsyncTimerCallback = workRequestData.Function == _asyncTimerFunction
                };

                curr = workRequestData.NextWorkRequest;
                if (curr == _firstLegacyWorkRequest)
                    break;
            }
        }

        /// <summary>
        /// Enumerates the ThreadPool's HillClimbing log.  This is the log of why we decided to add
        /// or remove threads from the ThreadPool.
        /// Note this is currently only supported on .Net Core and not Desktop CLR.
        /// </summary>
        /// <returns>An enumeration of the HillClimbing log, or an empty enumeration for Desktop CLR.</returns>
        public IEnumerable<HillClimbingLogEntry> EnumerateHillClimbingLog()
        {
            if (Portable)
            {
                ClrType? hillClimbingType = _runtime.BaseClassLibrary.GetTypeByName("System.Threading.PortableThreadPool+HillClimbing");
                ClrStaticField? hillClimberField = hillClimbingType?.GetStaticFieldByName("ThreadPoolHillClimber");
                if (hillClimberField is null)
                    yield break;

                ClrObject hillClimber = hillClimberField.ReadObject(GetDomain());

                int start = hillClimber.ReadField<int>("_logStart");
                int size = hillClimber.ReadField<int>("_logSize");
                ClrObject log = hillClimber.ReadObjectField("_log");

                ClrArray logArray = log.AsArray();
                size = Math.Min(size, logArray.Length);

                for (int i = 0; i < size; i++)
                {
                    int index = (i + start) % size;
                    ClrValueType logEntry = logArray.GetStructValue(index);

                    int tickCount = logEntry.ReadField<int>("tickCount");
                    HillClimbingTransition stateOrTransition = logEntry.ReadField<HillClimbingTransition>("stateOrTransition");
                    int newControlSetting = logEntry.ReadField<int>("newControlSetting");
                    int lastHistoryCount = logEntry.ReadField<int>("lastHistoryCount");
                    float lastHistoryMean = logEntry.ReadField<float>("lastHistoryMean");
                    yield return new HillClimbingLogEntry(tickCount, stateOrTransition, newControlSetting, lastHistoryCount, lastHistoryMean);
                }
            }
            else if (_nativeLogAddress != 0)
            {
                IDataReader reader = _runtime.DataTarget.DataReader;

                uint sizeOfLogEntry = (uint)Unsafe.SizeOf<NativeHillClimbingLogEntry>();
                for (uint i = 0; i < _nativeLogSize; i++)
                {
                    uint index = (i + _nativeLogStart) % _nativeLogSize;
                    ulong address = _nativeLogAddress + index * sizeOfLogEntry;

                    if (reader.Read(address, out NativeHillClimbingLogEntry entry))
                        yield return new HillClimbingLogEntry(entry);
                }
            }
        }

        private ClrObject GetPortableThreadPool(bool mustBePortable)
        {
            ClrModule bcl = _runtime.BaseClassLibrary;
            ClrType? threadPoolType = bcl.GetTypeByName("System.Threading.ThreadPool");
            if (threadPoolType is null)
                return default;

            ClrAppDomain domain = GetDomain();

            if (!mustBePortable)
            {
                ClrStaticField? usePortableThreadPoolField = threadPoolType.GetStaticFieldByName("UsePortableThreadPool");
                if (usePortableThreadPoolField is null)
                    return default;

                if (!usePortableThreadPoolField.Read<bool>(domain))
                    return default;
            }

            ClrType? portableThreadPoolType = bcl.GetTypeByName("System.Threading.PortableThreadPool");
            ClrStaticField? instanceField = portableThreadPoolType?.GetStaticFieldByName("ThreadPoolInstance");
            if (instanceField is null)
                return default;

            return instanceField.ReadObject(domain);
        }

        private ClrAppDomain GetDomain() => _runtime.SharedDomain ?? _runtime.SystemDomain ?? _runtime.AppDomains[0];

    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct NativeHillClimbingLogEntry
    {
        public readonly int TickCount;
        public readonly HillClimbingTransition Transition;
        public readonly int NewControlSetting;
        public readonly int LastHistoryCount;
        public readonly float LastHistoryMean;
    }

    /// <summary>
    /// An entry in the HillClimbing log.
    /// </summary>
    public class HillClimbingLogEntry
    {
        /// <summary>
        /// The tick count of this entry.
        /// </summary>
        public int TickCount { get; }

        /// <summary>
        /// The new state.
        /// </summary>
        public HillClimbingTransition StateOrTransition { get; }

        /// <summary>
        /// The new control setting.
        /// </summary>
        public int NewThreadCount { get; }

        /// <summary>
        /// The last history count.
        /// </summary>
        public int SampleCount { get; }

        /// <summary>
        /// The last history mean.
        /// </summary>
        public float Throughput { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public HillClimbingLogEntry(int tickCount, HillClimbingTransition stateOrTransition, int newThreadCount, int sampleCount, float throughput)
        {
            TickCount = tickCount;
            StateOrTransition = stateOrTransition;
            NewThreadCount = newThreadCount;
            SampleCount = sampleCount;
            Throughput = throughput;
        }

        internal HillClimbingLogEntry(NativeHillClimbingLogEntry entry)
        {
            TickCount = entry.TickCount;
            StateOrTransition = entry.Transition;
            NewThreadCount = entry.NewControlSetting;
            SampleCount = entry.LastHistoryCount;
            Throughput = entry.LastHistoryMean;
        }
    }
}