// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

// Consolidated marker debuggee for the modern SOS test harness. One process drives every "snapshot"
// scenario the harness needs, each at its own named TestHarness.Stop point (the harness self-collects a
// dump at each; live tests set a bpmd breakpoint on the same marker method). Scenarios are ordered so
// the heap scenario (live/dead objects) is captured before any GC runs for the generation-promotion
// stops. A fixed set of worker threads is parked at a known method for the whole run so the all-threads
// stop always sees them.
//
// Uniquely-named public marker types let tests find a specific object via `!dumpheap -type <T>`
// (statistics -> MT -> address) and cross-check it against -p/-l/-a slot values, -gc roots, and dso —
// an SOS-native value oracle with no ClrMD. Public const/static-readonly fields are mirrored into the
// test project by the source generator so thresholds aren't hard-coded twice.
public static class SosHarnessScenarios
{
    // ~192 KB: comfortably larger than every small object so dumpheap -min/-max can bracket it.
    // Mirrored into the test project (TestTargets.SosHarnessScenarios.BigArraySize) by the source gen.
    public const int BigArraySize = 0x30000;

    // Known primitive values held in args/locals across the argslocals stop (checked by clrstack -p/-l/-a).
    public const int ArgNumberValue = 0x2a;
    public const int LocalIntValue = 0x63;

    // A pinned array on the pinned object heap (for !dumpheap -gen poh and the POH eeheap segment).
    public const int PohArraySize = 0x4000;

    // Known field/struct/array values for the object-inspection commands (dumpobj fields, dumpvc, and
    // dumparray -details), mirrored into the test project by the source generator so the oracle isn't
    // duplicated. The known int[] holds element i == (i + 1) * KnownIntArrayElementStep.
    public const int FieldMarkerInt = 0x11223344;
    public const long FieldMarkerLong = 0x556677889AABBCCD;
    public const string FieldMarkerText = "field-marker-text";
    public const int ValueMarkerFirst = 0x0ABCDEF;
    public const long ValueMarkerSecond = 0x1122334455;
    public const int KnownIntArrayLength = 8;
    public const int KnownIntArrayElementStep = 0x11;

    private const int WorkerCount = 3;

    // WorkerCount workers + the main thread rendezvous here before any stop is taken.
    private static readonly Barrier s_ready = new(WorkerCount + 1);

    // Held until the end so workers (and the lock holder) stay parked across every dump/breakpoint.
    private static readonly ManualResetEventSlim s_release = new(initialState: false);

    // Thin-lock scenario: a dedicated thread takes an UNCONTENDED Monitor (lock) on this uniquely-typed
    // object and parks while holding it, so the object carries a THIN lock (owning thread id stamped in
    // the object header) — not inflated to a sync block — at the thinlock stop.
    private static readonly ThinLockMarker s_thinLock = new();
    private static readonly ManualResetEventSlim s_lockAcquired = new(initialState: false);

    // Rooted statics so SOS/ClrMD can always find the live objects under test.
    private static LiveUniqueMarker? s_live;
    private static byte[]? s_big;
#if !NETFRAMEWORK
    private static byte[]? s_poh;
#endif
    private static int[]? s_promoted;

    // Object-inspection oracle (known fields, struct field, and int[] field), live from the heap stop on.
    private static FieldMarker? s_fields;

    // Diagnostic-state oracles, live from the heap stop onward: a never-firing timer (!timerinfo), a
    // suspended async state machine + its gate (!dumpasync), and a populated ConcurrentDictionary (!dcd).
    private static Timer? s_timer;
    private static Task? s_asyncTask;
    private static readonly TaskCompletionSource<int> s_asyncGate = new();
    private static ConcurrentDictionary<int, string>? s_concurrentDictionary;
    private static ConcurrentQueue<int>? s_concurrentQueue;

    // A CONTENDED monitor for !syncblk: a holder parks while holding s_fatLock and a second thread blocks
    // acquiring it, inflating the lock to a sync block (unlike the uncontended thin lock above).
    private static readonly object s_fatLock = new();
    private static readonly ManualResetEventSlim s_fatLockHeld = new(initialState: false);

    [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
    private static void Main()
    {
        // Park WorkerCount worker threads at WorkerPark for the whole run.
        Thread[] workers = new Thread[WorkerCount];
        for (int i = 0; i < WorkerCount; i++)
        {
            workers[i] = new Thread(WorkerEntry) { IsBackground = false, Name = $"Worker{i}" };
            workers[i].Start();
        }

        // Proceed only once every worker is parked.
        s_ready.SignalAndWait();

        // Start the thin-lock holder (takes an uncontended Monitor on s_thinLock and parks while holding
        // it) and allocate the pinned (POH) array — both live from here on, so they are present at the
        // heap stop too. POH is a .NET 5+ concept, so Core/SingleFile only.
        Thread lockHolder = new(LockHolder) { IsBackground = false, Name = "LockHolder" };
        lockHolder.Start();
        s_lockAcquired.Wait();
#if !NETFRAMEWORK
        s_poh = GC.AllocateArray<byte>(PohArraySize, pinned: true);
#endif

        // 1. HEAP scenario FIRST (before any GC): a rooted live object, a known-large rooted array, and a
        //    deliberately-dropped dead object (still uncollected at the snapshot).
        LiveUniqueMarker live = new();
        s_live = live;
        byte[] big = new byte[BigArraySize];
        s_big = big;

        // Object-inspection oracles with known field/struct/array contents.
        int[] known = new int[KnownIntArrayLength];
        for (int i = 0; i < known.Length; i++)
        {
            known[i] = (i + 1) * KnownIntArrayElementStep;
        }

        s_fields = new FieldMarker
        {
            IntField = FieldMarkerInt,
            LongField = FieldMarkerLong,
            TextField = FieldMarkerText,
            Value = new ValueMarker { First = ValueMarkerFirst, Second = ValueMarkerSecond },
            Numbers = known,
            ObjectReferences = new[] { new ObjectReference { Value = live } },
            MethodSignature = new byte[] { 0x00, 0x00, 0x01 }, // [DEFAULT] Void ()
            SignatureElement = new byte[] { 0x08 },            // ELEMENT_TYPE_I4
        };

        // Diagnostic-state oracles, all live at the heap stop: a never-firing timer (!timerinfo), a parked
        // thread-pool work item (!threadpool), a suspended async state machine (!dumpasync), a populated
        // ConcurrentDictionary (!dcd), and a contended monitor that inflates to a sync block (!syncblk).
        s_timer = new Timer(_ => { }, null, dueTime: 3_600_000, period: Timeout.Infinite);
        ThreadPool.QueueUserWorkItem(_ => s_release.Wait());
        s_asyncTask = SuspendedAsync();
        s_concurrentDictionary = new ConcurrentDictionary<int, string>();
        s_concurrentDictionary[1] = "one";
        s_concurrentDictionary[2] = "two";
        s_concurrentDictionary[3] = "three";

        s_concurrentQueue = new ConcurrentQueue<int>();
        s_concurrentQueue.Enqueue(0x111);
        s_concurrentQueue.Enqueue(0x222);
        s_concurrentQueue.Enqueue(0x333);

        Thread fatLockHolder = new(FatLockHolder) { IsBackground = false, Name = "FatLockHolder" };
        fatLockHolder.Start();
        s_fatLockHeld.Wait();
        Thread fatLockWaiter = new(FatLockWaiter) { IsBackground = true, Name = "FatLockWaiter" };
        fatLockWaiter.Start();
        Thread.Sleep(250); // let the waiter block on s_fatLock so the monitor inflates to a sync block

        AllocateDead();
        AtHeap();

        // 2. THINLOCK stop: the lock holder still holds the thin lock (and the POH array is live).
        AtThinLock();

        // 2. ARGSLOCALS scenario: known primitive + uniquely-typed reference args/locals held live.
        ArgsLocalsMethod(ArgNumberValue, new ArgUniqueMarker());

        // 3. ROOTS scenario: a normal object, a pinned interior pointer, and a plain interior pointer.
        RootsScenario();

        // 4. GC PROMOTION scenario: promote one rooted array gen0 -> gen1 -> gen2, stopping at each.
        int[] promoted = new int[100];
        s_promoted = promoted;
        AtGen0();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        AtGen1();
        GC.Collect(2);
        GC.WaitForPendingFinalizers();
        AtGen2();

        // 5. ALLTHREADS scenario: workers are still parked at WorkerPark.
        AtAllThreads();

        // Release the workers (and the lock holder) so the process exits cleanly.
        s_release.Set();
        foreach (Thread w in workers)
        {
            w.Join();
        }

        lockHolder.Join();
        fatLockHolder.Join();

        GC.KeepAlive(live);
        GC.KeepAlive(big);
        GC.KeepAlive(promoted);
        GC.KeepAlive(s_live);
        GC.KeepAlive(s_big);
#if !NETFRAMEWORK
        GC.KeepAlive(s_poh);
#endif
        GC.KeepAlive(s_promoted);
        GC.KeepAlive(s_thinLock);
        GC.KeepAlive(s_fields);
        GC.KeepAlive(s_timer);
        GC.KeepAlive(s_asyncTask);
        GC.KeepAlive(s_concurrentDictionary);
        GC.KeepAlive(s_concurrentQueue);
    }

    // --- Diagnostic-state scenario helpers ---

    // Suspends forever at the await (the gate is never completed), so a suspended async state machine is
    // present on the heap for !dumpasync.
    private static async Task SuspendedAsync() => await s_asyncGate.Task.ConfigureAwait(false);

    // Holds s_fatLock and parks; combined with FatLockWaiter blocking on the same lock this inflates the
    // monitor to a sync block for !syncblk.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FatLockHolder()
    {
        lock (s_fatLock)
        {
            s_fatLockHeld.Set();
            s_release.Wait();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FatLockWaiter()
    {
        lock (s_fatLock)
        {
        }
    }

    // --- Thin-lock scenario ---

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LockHolder()
    {
        // Take the lock and park while holding it. No other thread contends for s_thinLock and we never
        // Wait/GetHashCode on it, so it stays a thin lock (no sync-block inflation).
        lock (s_thinLock)
        {
            s_lockAcquired.Set();
            s_release.Wait();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AtThinLock() => TestHarness.Stop("thinlock");

    // --- Heap scenario ---

    // Allocate a DeadUniqueMarker and let the only reference die when this method returns. No
    // GC.KeepAlive and no GC.Collect before the snapshot, so at the heap stop it is unreachable (dead)
    // but not yet collected.
    [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
    private static void AllocateDead()
    {
        DeadUniqueMarker dead = new();
        _ = dead;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AtHeap() => TestHarness.Stop("heap");

    // --- Args/locals scenario ---

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ArgsLocalsMethod(int number, ArgUniqueMarker arg)
    {
        int localInt = LocalIntValue;
        LocalUniqueMarker localObj = new();

        AtArgsLocals();

        // Keep every slot live across the stop so SOS can read the values.
        if (number != ArgNumberValue)
        {
            throw new Exception("unreachable");
        }

        if (localInt != LocalIntValue)
        {
            throw new Exception("unreachable");
        }

        GC.KeepAlive(arg);
        GC.KeepAlive(localObj);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AtArgsLocals() => TestHarness.Stop("argslocals");

    // --- GC roots scenario ---

    private static unsafe void RootsScenario()
    {
        object normal = new();
        byte[] buffer = new byte[256];
        int[] numbers = new int[] { 10, 20, 30, 40 };

        fixed (byte* pinned = buffer)
        {
            ref int interior = ref numbers[2];

            AtRoots();

            // Touch everything so none of it is optimized away or collected before the marker.
            *pinned = (byte)interior;
            GC.KeepAlive(normal);
            GC.KeepAlive(buffer);
            GC.KeepAlive(numbers);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AtRoots() => TestHarness.Stop("roots");

    // --- GC promotion scenario ---

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AtGen0() => TestHarness.Stop("gen0");

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AtGen1() => TestHarness.Stop("gen1");

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AtGen2() => TestHarness.Stop("gen2");

    // --- All-threads scenario ---

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WorkerEntry() => WorkerPark();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WorkerPark()
    {
        // Rendezvous with main, then block here so this thread's managed stack stays in WorkerPark when
        // any stop is taken.
        s_ready.SignalAndWait();
        s_release.Wait();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AtAllThreads() => TestHarness.Stop("allthreads");
}

public sealed class LiveUniqueMarker
{
}

public sealed class DeadUniqueMarker
{
}

public sealed class ArgUniqueMarker
{
}

public sealed class LocalUniqueMarker
{
}

public sealed class ThinLockMarker
{
}

// A value type with two known fields, embedded in FieldMarker so dumpvc can be exercised on the inline
// value-class instance (its address comes from dumpobj's Value column for the Value field).
public struct ValueMarker
{
    public int First;
    public long Second;
}

// A value-type array element containing exactly one object-reference slot. !dumparray reports the element's
// address, giving notreachableinrange a real pointer range to scan without relying on object layout.
public struct ObjectReference
{
    public object? Value;
}

// A reference type with known instance fields of several shapes (primitive, wide primitive, reference,
// and an embedded value type), so dumpobj prints a non-trivial Fields table to assert against.
public sealed class FieldMarker
{
    public int IntField;
    public long LongField;
    public string? TextField;
    public ValueMarker Value;
    public int[]? Numbers;
    public ObjectReference[]? ObjectReferences;

    // Raw COR_SIGNATURE blobs so dumpsig/dumpsigelem can decode a real signature: a method signature
    // [DEFAULT] Void () = { CALLCONV_DEFAULT(0x00), argCount 0, ELEMENT_TYPE_VOID(0x01) }, and a single
    // ELEMENT_TYPE_I4(0x08) element for dumpsigelem. Held in a byte[] so a test can take the address of
    // the first byte via dumparray.
    public byte[]? MethodSignature;
    public byte[]? SignatureElement;
}
