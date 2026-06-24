// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.StressLogs;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    /// <summary>A single GC heap relocation message (GcPlugMove).</summary>
    internal readonly struct PlugRecord
    {
        public PlugRecord(ulong plugStart, ulong plugEnd, ulong delta)
        {
            PlugStart = plugStart;
            PlugEnd = plugEnd;
            Delta = delta;
        }

        public ulong PlugStart { get; }
        public ulong PlugEnd { get; }
        public ulong Delta { get; }
    }

    /// <summary>A single GC root relocation message (GcRoot).</summary>
    internal readonly struct RelocRecord
    {
        public RelocRecord(ulong root, ulong prevValue, ulong newValue, ulong methodTable)
        {
            Root = root;
            PrevValue = prevValue;
            NewValue = newValue;
            MethodTable = methodTable;
        }

        public ulong Root { get; }
        public ulong PrevValue { get; }
        public ulong NewValue { get; }
        public ulong MethodTable { get; }
    }

    /// <summary>A single GC root promotion message (GcRootPromote).</summary>
    internal readonly struct PromoteRecord
    {
        public PromoteRecord(ulong root, ulong value, ulong methodTable)
        {
            Root = root;
            Value = value;
            MethodTable = methodTable;
        }

        public ulong Root { get; }
        public ulong Value { get; }
        public ulong MethodTable { get; }
    }

    /// <summary>
    /// All relocation/promotion/plug records collected for one GC, keyed by the
    /// GC index reported in the stress log's BEGINGC message.
    /// </summary>
    internal sealed class GCRecord
    {
        public ulong GCCount { get; set; }
        public List<PlugRecord> Plugs { get; } = new();
        public List<RelocRecord> Relocs { get; } = new();
        public List<PromoteRecord> Promotes { get; } = new();
    }

    /// <summary>
    /// Reconstructs and caches GC relocation/promotion history from the runtime's
    /// stress log. Populated by <c>!histinit</c>, consumed by the other
    /// <c>!hist*</c> commands, and released by <c>!histclear</c>. The reconstruction
    /// mirrors the native GcHistAddLog walk: the stress log is enumerated
    /// newest-first, plug/reloc/promote messages accumulate into the current GC,
    /// and a BEGINGC (GcStart) message commits and closes that GC.
    /// </summary>
    [ServiceExport(Scope = ServiceScope.Runtime)]
    public sealed class GCHistory
    {
        // Matches the native MAX_GCRECORDS cap in gchist.cpp.
        private const int MaxRecords = 500;

        private readonly List<GCRecord> _records = new();

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public IConsoleService Console { get; set; }

        /// <summary>True once <see cref="Initialize"/> has successfully run.</summary>
        public bool IsInitialized { get; private set; }

        internal IReadOnlyList<GCRecord> Records => _records;

        // Stress log header summary captured during Initialize, surfaced by !histinit.
        internal uint? FacilitiesToLog { get; private set; }
        internal uint? LevelToLog { get; private set; }
        internal uint? MaxSizePerThread { get; private set; }
        internal uint? MaxSizeTotal { get; private set; }
        internal int? ChunkCount { get; private set; }
        internal ulong TickFrequency { get; private set; }
        internal DateTime? StartTimeUtc { get; private set; }
        internal int ThreadCount { get; private set; }
        internal double ElapsedSeconds { get; private set; }
        internal int MessageCount { get; private set; }

        /// <summary>
        /// Reads the runtime's stress log and rebuilds the GC history. Returns
        /// <see langword="null"/> on success, or a human-readable failure reason.
        /// </summary>
        internal string Initialize()
        {
            Clear();

            if (!Runtime.TryGetStressLog(out StressLog stressLog, out string failureReason))
            {
                return failureReason;
            }

            FacilitiesToLog = stressLog.FacilitiesToLog;
            LevelToLog = stressLog.LevelToLog;
            MaxSizePerThread = stressLog.MaxSizePerThread;
            MaxSizeTotal = stressLog.MaxSizeTotal;
            ChunkCount = stressLog.ChunkCount;
            TickFrequency = stressLog.TickFrequency;
            StartTimeUtc = stressLog.StartTimeUtc;

            HashSet<ulong> threads = new();
            GCRecord current = new();

            foreach (StressLogMessage message in stressLog.EnumerateMessages(Console.CancellationToken))
            {
                threads.Add(message.OSThreadId);
                MessageCount++;
                if (message.ElapsedSeconds > ElapsedSeconds)
                {
                    ElapsedSeconds = message.ElapsedSeconds;
                }

                switch (message.KnownFormat)
                {
                    case StressLogKnownFormat.GcPlugMove:
                        current.Plugs.Add(new PlugRecord(message.GetArgument(0), message.GetArgument(1), message.GetArgument(2)));
                        break;

                    case StressLogKnownFormat.GcRoot:
                        current.Relocs.Add(new RelocRecord(message.GetArgument(0), message.GetArgument(1), message.GetArgument(2), message.GetArgument(3)));
                        break;

                    case StressLogKnownFormat.GcRootPromote:
                        current.Promotes.Add(new PromoteRecord(message.GetArgument(0), message.GetArgument(1), message.GetArgument(2)));
                        break;

                    case StressLogKnownFormat.GcStart:
                        // BEGINGC closes the GC we have been accumulating (the log is
                        // newest-first, so the start is the oldest message of the GC).
                        current.GCCount = message.GetArgument(0);
                        if (_records.Count < MaxRecords)
                        {
                            _records.Add(current);
                        }

                        current = new GCRecord();
                        break;

                    default:
                        break;
                }
            }

            ThreadCount = threads.Count;
            IsInitialized = true;
            return null;
        }

        /// <summary>Releases all cached GC history. Mirrors native GcHistClear.</summary>
        public void Clear()
        {
            _records.Clear();
            IsInitialized = false;
            FacilitiesToLog = null;
            LevelToLog = null;
            MaxSizePerThread = null;
            MaxSizeTotal = null;
            ChunkCount = null;
            TickFrequency = 0;
            StartTimeUtc = null;
            ThreadCount = 0;
            ElapsedSeconds = 0;
            MessageCount = 0;
        }
    }
}
