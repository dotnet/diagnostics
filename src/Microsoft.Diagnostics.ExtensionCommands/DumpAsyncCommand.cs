// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = CommandName, Aliases = new string[] { "DumpAsync" }, Help = "Displays information about async \"stacks\" on the garbage-collected heap.")]
    public sealed class DumpAsyncCommand : ExtensionCommandBase
    {
        /// <summary>The name of the command.</summary>
        private const string CommandName = "dumpasync";

        /// <summary>Indent width.</summary>
        private const int TabWidth = 2;
        /// <summary>The command invocation syntax when used in Debugger Markup Language (DML) commands.</summary>
        private const string DmlCommandInvoke = $"!{CommandName}";

        /// <summary>The help text to render when asked for help.</summary>
        private static readonly string s_detailedHelpText =
            $"Usage: {CommandName} [--stats] [--coalesce] [--address <object address>] [--methodtable <mt address>] [--type <partial type name>] [--tasks] [--completed] [--fields]" + Environment.NewLine +
            Environment.NewLine +
            "Displays information about async \"stacks\" on the garbage-collected heap. Stacks" + Environment.NewLine +
            "are synthesized by finding all task objects (including async state machine box" + Environment.NewLine +
            "objects) on the GC heap and chaining them together based on continuations." + Environment.NewLine +
            Environment.NewLine +
            "Options:" + Environment.NewLine +
            "  --stats        Summarize all async frames found rather than showing detailed stacks." + Environment.NewLine +
            "  --coalesce     Coalesce stacks and portions of stacks that are the same." + Environment.NewLine +
            "  --address      Only show stacks that include the object with the specified address." + Environment.NewLine +
            "  --methodtable  Only show stacks that include objects with the specified method table." + Environment.NewLine +
            "  --type         Only show stacks that include objects whose type includes the specified name in its name." + Environment.NewLine +
            "  --tasks        Include stacks that contain only non-state machine task objects." + Environment.NewLine +
            "  --completed    Include completed tasks in stacks." + Environment.NewLine +
            "  --fields       Show fields for each async stack frame." + Environment.NewLine +
            Environment.NewLine +
            "Examples:" + Environment.NewLine +
            $"Summarize all async frames associated with a specific method table address:        !{CommandName} --stats --methodtable 0x00007ffbcfbe0970" + Environment.NewLine +
            $"Show all stacks coalesced by common frames:                                        !{CommandName} --coalesce" + Environment.NewLine +
            $"Show each stack that includes \"ReadAsync\":                                         !{CommandName} --type ReadAsync" + Environment.NewLine +
            $"Show each stack that includes an object at a specific address, and include fields: !{CommandName} --address 0x000001264adce778 --fields";

        /// <summary>Gets the runtime for the process.  Set by the command framework.</summary>
        [ServiceImport(Optional = true)]
        public ClrRuntime? Runtime { get; set; }

        /// <summary>Gets whether to only show stacks that include the object with the specified address.</summary>
        [Option(Name = "--address", Aliases = new string[] { "-addr" }, Help = "Only show stacks that include the object with the specified address.")]
        public ulong? ObjectAddress { get; set; }

        /// <summary>Gets whether to only show stacks that include objects with the specified method table.</summary>
        [Option(Name = "--methodtable", Aliases = new string[] { "-mt" }, Help = "Only show stacks that include objects with the specified method table.")]
        public ulong? MethodTableAddress { get; set; }

        /// <summary>Gets whether to only show stacks that include objects whose type includes the specified name in its name.</summary>
        [Option(Name = "--type", Help = "Only show stacks that include objects whose type includes the specified name in its name.")]
        public string? NameSubstring { get; set; }

        /// <summary>Gets whether to include stacks that contain only non-state machine task objects.</summary>
        [Option(Name = "--tasks", Aliases = new string[] { "-t" }, Help = "Include stacks that contain only non-state machine task objects.")]
        public bool IncludeTasks { get; set; }

        /// <summary>Gets whether to include completed tasks in stacks.</summary>
        [Option(Name = "--completed", Aliases = new string[] { "-c" }, Help = "Include completed tasks in stacks.")]
        public bool IncludeCompleted { get; set; }

        /// <summary>Gets whether to show state machine fields for every async stack frame that has them.</summary>
        [Option(Name = "--fields", Aliases = new string[] { "-f" }, Help = "Show state machine fields for every async stack frame that has them.")]
        public bool DisplayFields { get; set; }

        /// <summary>Gets whether to summarize all async frames found rather than showing detailed stacks.</summary>
        [Option(Name = "--stats", Help = "Summarize all async frames found rather than showing detailed stacks.")]
        public bool Summarize { get; set; }

        /// <summary>Gets whether to coalesce stacks and portions of stacks that are the same.</summary>
        [Option(Name = "--coalesce", Help = "Coalesce stacks and portions of stacks that are the same.")]
        public bool CoalesceStacks { get; set; }

        /// <summary>Invokes the command.</summary>
        public override void ExtensionInvoke()
        {
            ClrRuntime? runtime = Runtime;
            if (runtime is null)
            {
                WriteLineError("Unable to access runtime.");
                return;
            }

            ClrHeap heap = runtime.Heap;
            if (!heap.CanWalkHeap)
            {
                WriteLineError("Unable to examine the heap.");
                return;
            }

            ClrType? taskType = runtime.BaseClassLibrary.GetTypeByName("System.Threading.Tasks.Task");
            if (taskType is null)
            {
                WriteLineError("Unable to find required type.");
                return;
            }

            ClrStaticField? taskCompletionSentinelType = taskType.GetStaticFieldByName("s_taskCompletionSentinel");

            ClrObject taskCompletionSentinel = default;

            if (taskCompletionSentinelType is not null)
            {
                Debug.Assert(taskCompletionSentinelType.IsObjectReference);
                taskCompletionSentinel = taskCompletionSentinelType.ReadObject(runtime.BaseClassLibrary.AppDomain);
            }

            // Enumerate the heap, gathering up all relevant async-related objects.
            Dictionary<ClrObject, AsyncObject> objects = CollectObjects();

            // Render the data according to the options specified.
            if (Summarize)
            {
                RenderStats();
            }
            else if (CoalesceStacks)
            {
                RenderCoalescedStacks();
            }
            else
            {
                RenderStacks();
            }
            return;

            // <summary>Group frames and summarize how many of each occurred.</summary>
            void RenderStats()
            {
                // Enumerate all of the "frames", and create a mapping from a rendering of that
                // frame to its associated type and how many times that frame occurs.
                var typeCounts = new Dictionary<string, (ClrType Type, int Count)>();
                foreach (KeyValuePair<ClrObject, AsyncObject> pair in objects)
                {
                    ClrObject obj = pair.Key;
                    if (obj.Type is null)
                    {
                        continue;
                    }

                    string description = Describe(obj);

                    if (!typeCounts.TryGetValue(description, out (ClrType Type, int Count) value))
                    {
                        value = (obj.Type, 0);
                    }
                    
                    value.Count++;
                    typeCounts[description] = value;
                }

                // Render one line per frame.
                WriteHeaderLine($"{"MT",-16} {"Count",-8} Type");
                foreach (KeyValuePair<string, (ClrType Type, int Count)> entry in typeCounts.OrderByDescending(e => e.Value.Count))
                {
                    WriteMethodTable(entry.Value.Type.MethodTable, asyncObject: true);
                    WriteLine($" {entry.Value.Count,-8:N0} {entry.Key}");
                }
            }

            // <summary>Group stacks at each frame in order to render a tree of coalesced stacks.</summary>
            void RenderCoalescedStacks()
            {
                // Find all stacks to include.
                var startingList = new List<ClrObject>();
                foreach (KeyValuePair<ClrObject, AsyncObject> entry in objects)
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    AsyncObject obj = entry.Value;
                    if (obj.TopLevel && ShouldIncludeStack(obj))
                    {
                        startingList.Add(entry.Key);
                    }
                }

                // If we found any, render them.
                if (startingList.Count > 0)
                {
                    RenderLevel(startingList, 0);
                }

                // <summary>Renders the next level of frames for coalesced stacks.</summary>
                void RenderLevel(List<ClrObject> frames, int depth)
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();
                    List<ClrObject> nextLevel = new List<ClrObject>();

                    // Grouping function.  We want to treat all objects that render the same as the same entity.
                    // For async state machines, we include the await state, both because we want it to render
                    // and because we want to see state machines at different positions as part of different groups.
                    Func<ClrObject, string> groupBy = o =>
                    {
                        string description = Describe(o);
                        if (objects.TryGetValue(o, out AsyncObject asyncObject) && asyncObject.IsStateMachine)
                        {
                            description = $"({asyncObject.AwaitState}) {description}";
                        }
                        return description;
                    };

                    // Group all of the frames, rendering each group as a single line with a count.
                    // Then recur for each.
                    int stackId = 1;
                    foreach (IGrouping<string, ClrObject> group in frames.GroupBy(groupBy).OrderByDescending(g => g.Count()))
                    {
                        int count = group.Count();
                        Debug.Assert(count > 0);

                        // For top-level frames, write out a header.
                        if (depth == 0)
                        {
                            WriteHeaderLine($"STACKS {stackId++}");
                        }

                        // Write out the count and frame.
                        Write($"{Tabs(depth)}[{count}] ");
                        WriteMethodTable(group.First().Type?.MethodTable ?? 0, asyncObject: true);
                        WriteLine($" {group.Key}");

                        // Gather up all of the next level of frames.
                        nextLevel.Clear();
                        foreach (ClrObject next in group)
                        {
                            if (objects.TryGetValue(next, out AsyncObject asyncObject))
                            {
                                // Note that the merging of multiple continuations can lead to numbers increasing at a particular
                                // level of the coalesced stacks.  It's not clear there's a better answer.
                                nextLevel.AddRange(asyncObject.Continuations);
                            }
                        }

                        // If we found any, recur.
                        if (nextLevel.Count != 0)
                        {
                            RenderLevel(nextLevel, depth + 1);
                        }

                        if (depth == 0)
                        {
                            WriteLine("");
                        }
                    }
                }
            }

            // <summary>Render each stack of frames.</summary>
            void RenderStacks()
            {
                var stack = new Stack<(AsyncObject AsyncObject, int Depth)>();

                // Find every top-level object (ones that nothing else has as a continuation) and output
                // a stack starting from each.
                int stackId = 1;
                foreach (KeyValuePair<ClrObject, AsyncObject> entry in objects)
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();
                    AsyncObject top = entry.Value;
                    if (!top.TopLevel || !ShouldIncludeStack(top))
                    {
                        continue;
                    }

                    int depth = 0;

                    WriteHeaderLine($"STACK {stackId++}");

                    // If the top-level frame is an async method that's paused at an await, it must be waiting on
                    // something.  Try to synthesize a frame to represent that thing, just to provide a little more information.
                    if (top.IsStateMachine && top.AwaitState >= 0 && !IsCompleted(top.TaskStateFlags) &&
                        top.StateMachine is IClrValue stateMachine &&
                        stateMachine.Type is not null)
                    {
                        // Short of parsing the method's IL, we don't have a perfect way to know which awaiter field
                        // corresponds to the current await state, as awaiter fields are shared across all awaits that
                        // use the same awaiter type.  We instead employ a heuristic.  If the await state is 0, the
                        // associated field will be the first one (<>u__1); even if other awaits share it, it's fine
                        // to use.  Similarly, if there's only one awaiter field, we know that must be the one being
                        // used.  In all other situations, we can't know which of the multiple awaiter fields maps
                        // to the await state, so we instead employ a heuristic of looking for one that's non-zero.
                        // The C# compiler zero's out awaiter fields when it's done with them, so if we find an awaiter
                        // field with any non-zero bytes, it must be the one in use.  This can have false negatives,
                        // as it's perfectly valid for an awaiter to be all zero bytes, but it's better than nothing.

                        if ((top.AwaitState == 0) ||
                            stateMachine.Type.Fields.Count(f => f.Name is null || f.Name.StartsWith("<>u__", StringComparison.Ordinal) == true) == 1) // if the name is null, we have to assume it's an awaiter
                        {
                            if (stateMachine.Type.GetFieldByName("<>u__1") is ClrInstanceField field &&
                                TrySynthesizeAwaiterFrame(field))
                            {
                                depth++;
                            }
                        }
                        else
                        {
                            foreach (ClrInstanceField field in stateMachine.Type.Fields)
                            {
                                // Look for awaiter fields.  This is the naming convention employed by the C# compiler.
                                if (field.Name?.StartsWith("<>u__") == true)
                                {
                                    if (field.IsObjectReference)
                                    {
                                        if (stateMachine.ReadObjectField(field.Name) is ClrObject { IsNull: false } awaiter)
                                        {
                                            if (TrySynthesizeAwaiterFrame(field))
                                            {
                                                depth++;
                                            }
                                            break;
                                        }
                                    }
                                    else if (field.IsValueType &&
                                        stateMachine.ReadValueTypeField(field.Name) is ClrValueType { IsValid: true } awaiter &&
                                        awaiter.Type is not null)
                                    {
                                        byte[] awaiterBytes = new byte[awaiter.Type.StaticSize - (runtime.DataTarget!.DataReader.PointerSize * 2)];
                                        if (runtime.DataTarget!.DataReader.Read(awaiter.Address, awaiterBytes) == awaiterBytes.Length && !AllZero(awaiterBytes))
                                        {
                                            if (TrySynthesizeAwaiterFrame(field))
                                            {
                                                depth++;
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        // <summary>Writes out a frame for the specified awaiter field, if possible.</summary>
                        bool TrySynthesizeAwaiterFrame(ClrInstanceField field)
                        {
                            if (field?.Name is string name)
                            {
                                if (field.IsObjectReference)
                                {
                                    IClrValue awaiter = stateMachine.ReadObjectField(name);
                                    if (awaiter.Type is not null)
                                    {
                                        Write("<< Awaiting: ");
                                        WriteAddress(awaiter.Address, asyncObject: false);
                                        Write(" ");
                                        WriteMethodTable(awaiter.Type.MethodTable, asyncObject: false);
                                        Write(awaiter.Type.Name);
                                        WriteLine(" >>");
                                        return true;
                                    }
                                }
                                else if (field.IsValueType)
                                {
                                    IClrValue awaiter = stateMachine.ReadValueTypeField(name);
                                    if (awaiter.Type is not null)
                                    {
                                        Write("<< Awaiting: ");
                                        WriteValueTypeAddress(awaiter.Address, awaiter.Type.MethodTable);
                                        Write(" ");
                                        WriteMethodTable(awaiter.Type.MethodTable, asyncObject: false);
                                        Write($" {awaiter.Type.Name}");
                                        WriteLine(" >>");
                                        return true;
                                    }
                                }
                            }

                            return false;
                        }
                    }

                    // Push the root node onto the stack to start the iteration.  Then as long as there are nodes left
                    // on the stack, pop the next, render it, and push any continuations it may have back onto the stack.
                    Debug.Assert(stack.Count == 0);
                    stack.Push((top, depth));
                    while (stack.Count > 0)
                    {
                        (AsyncObject frame, depth) = stack.Pop();

                        Write($"{Tabs(depth)}");
                        WriteAddress(frame.Object.Address, asyncObject: true);
                        Write(" ");
                        WriteMethodTable(frame.Object.Type?.MethodTable ?? 0, asyncObject: true);
                        Write($" {(frame.IsStateMachine ? $"({frame.AwaitState})" : $"({DescribeTaskFlags(frame.TaskStateFlags)})")} {Describe(frame.Object)}");
                        WriteCodeLink(frame.NativeCode);
                        WriteLine("");

                        if (DisplayFields)
                        {
                            RenderFields(frame.StateMachine ?? frame.Object, depth + 4); // +4 for extra indent for fields
                        }

                        foreach (ClrObject continuation in frame.Continuations)
                        {
                            if (objects.TryGetValue(continuation, out AsyncObject asyncContinuation))
                            {
                                stack.Push((asyncContinuation, depth + 1));
                            }
                            else
                            {
                                string state = TryGetTaskStateFlags(continuation, out int flags) ? DescribeTaskFlags(flags) : "";
                                Write($"{Tabs(depth + 1)}");
                                WriteAddress(continuation.Address, asyncObject: true);
                                Write(" ");
                                WriteMethodTable(continuation.Type?.MethodTable ?? 0, asyncObject: true);
                                WriteLine($" ({state}) {Describe(continuation)}");
                            }
                        }
                    }

                    WriteLine("");
                }
            }

            // <summary>Determine whether the stack rooted in this object should be rendered.</summary>
            bool ShouldIncludeStack(AsyncObject obj)
            {
                // We want to render the stack for this object once we find any node that should be
                // included based on the criteria specified as arguments _and_ if the include tasks
                // options wasn't specified, once we find any node that's an async state machine.
                // That way, we scope the output down to just stacks that contain something the
                // user is interested in seeing.
                bool sawShouldInclude = false;
                bool sawStateMachine = IncludeTasks;

                var stack = new Stack<AsyncObject>();
                stack.Push(obj);
                while (stack.Count > 0)
                {
                    obj = stack.Pop();
                    sawShouldInclude |= obj.IncludeInOutput;
                    sawStateMachine |= obj.IsStateMachine;

                    if (sawShouldInclude && sawStateMachine)
                    {
                        return true;
                    }

                    foreach (ClrObject continuation in obj.Continuations)
                    {
                        if (objects.TryGetValue(continuation, out AsyncObject asyncContinuation))
                        {
                            stack.Push(asyncContinuation);
                        }
                    }
                }

                return false;
            }

            // <summary>Outputs a line of information for each instance field on the object.</summary>
            void RenderFields(IClrValue? obj, int depth)
            {
                if (obj?.Type is not null)
                {
                    string depthTab = new string(' ', depth * TabWidth);

                    WriteHeaderLine($"{depthTab}{"Address",16} {"MT",16} {"Type",-32} {"Value",16} Name");
                    foreach (ClrInstanceField field in obj.Type.Fields)
                    {
                        if (field.Type is not null)
                        {
                            Write($"{depthTab}");
                            if (field.IsObjectReference)
                            {
                                ClrObject objRef = field.ReadObject(obj.Address, obj.Type.IsValueType);
                                WriteAddress(objRef.Address, asyncObject: false);
                            }
                            else
                            {
                                WriteValueTypeAddress(field.GetAddress(obj.Address, obj.Type.IsValueType), field.Type.MethodTable);
                            }
                            Write(" ");
                            WriteMethodTable(field.Type.MethodTable, asyncObject: false);
                            WriteLine($" {Truncate(field.Type.Name, 32),-32} {Truncate(GetDisplay(obj, field).ToString(), 16),16} {field.Name}");
                        }
                    }
                }
            }

            // <summary>Gets a printable description for the specified object.</summary>
            string Describe(ClrObject obj)
            {
                string description = string.Empty;
                if (obj.Type?.Name is not null)
                {
                    // Default the description to the type name.
                    description = obj.Type.Name;

                    if (IsStateMachineBox(obj.Type))
                    {
                        // Remove the boilerplate box type from the name.
                        int pos = description.IndexOf("StateMachineBox<", StringComparison.Ordinal);
                        if (pos >= 0)
                        {
                            ReadOnlySpan<char> slice = description.AsSpan(pos + "StateMachineBox<".Length);
                            slice = slice.Slice(0, slice.Length - 1); // remove trailing >
                            description = slice.ToString();
                        }
                    }
                    else if (TryGetValidObjectField(obj, "m_action", out ClrObject taskDelegate))
                    {
                        // If we can figure out what the task's delegate points to, append the method signature.
                        if (TryGetMethodFromDelegate(runtime, taskDelegate, out ClrMethod? method))
                        {
                            description = $"{description} {{{method!.Signature}}}";
                        }
                    }
                    else if (obj.Address != 0 && taskCompletionSentinel.Address == obj.Address)
                    {
                        description = "TaskCompletionSentinel";
                    }
                }
                return description;
            }

            // <summary>Determines whether the specified object is of interest to the user based on their criteria provided as command arguments.</summary>
            bool IncludeInOutput(ClrObject obj)
            {
                if (ObjectAddress is ulong addr && obj.Address != addr)
                {
                    return false;
                }

                if (obj.Type is not null)
                {
                    if (MethodTableAddress is ulong mt && obj.Type.MethodTable != mt)
                    {
                        return false;
                    }

                    if (NameSubstring is not null && obj.Type.Name is not null && !obj.Type.Name.Contains(NameSubstring))
                    {
                        return false;
                    }
                }

                return true;
            }

            // <summary>Finds all of the relevant async-related objects on the heap.</summary>
            Dictionary<ClrObject, AsyncObject> CollectObjects()
            {
                var found = new Dictionary<ClrObject, AsyncObject>();

                // Enumerate the heap, looking for all relevant objects.
                foreach (ClrObject obj in heap.EnumerateObjects())
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    if (!obj.IsValid || obj.Type is null)
                    {
                        Trace.TraceError($"(Skipping invalid object {obj})");
                        continue;
                    }

                    // Skip objects too small to be state machines or tasks, simply to help with performance.
                    if (obj.Size <= 24)
                    {
                        continue;
                    }

                    // We only care about task-related objects (all boxes are tasks).
                    if (!IsTask(obj.Type))
                    {
                        continue;
                    }

                    // This is currently working around an issue that result in enumerating segments multiple times in 6.0 runtimes
                    // up to 6.0.5. The PR that fixes it is https://github.com/dotnet/runtime/pull/67995, but we have this here for back compat.
                    if (found.ContainsKey(obj))
                    {
                        continue;
                    }

                    // If we're only going to render a summary (which only considers objects individually and not
                    // as part of chains) and if this object shouldn't be included, we don't need to do anything more.
                    if (Summarize &&
                        (!IncludeInOutput(obj) || (!IncludeTasks && !IsStateMachineBox(obj.Type))))
                    {
                        continue;
                    }

                    // If we couldn't get state flags for the task, something's wrong; skip it.
                    if (!TryGetTaskStateFlags(obj, out int taskStateFlags))
                    {
                        continue;
                    }

                    // If we're supposed to ignore already completed tasks and this one is completed, skip it.
                    if (!IncludeCompleted && IsCompleted(taskStateFlags))
                    {
                        continue;
                    }

                    // Gather up the necessary data for the object and store it.
                    AsyncObject result = new()
                    {
                        Object = obj,
                        IsStateMachine = IsStateMachineBox(obj.Type),
                        IncludeInOutput = IncludeInOutput(obj),
                        TaskStateFlags = taskStateFlags,
                    };

                    if (result.IsStateMachine && TryGetStateMachine(obj, out result.StateMachine))
                    {
                        bool gotState = TryRead(result.StateMachine!, "<>1__state", out result.AwaitState);
                        Debug.Assert(gotState);

                        if (result.StateMachine?.Type is ClrType stateMachineType)
                        {
                            foreach (ClrMethod method in stateMachineType.Methods)
                            {
                                if (method.NativeCode != ulong.MaxValue)
                                {
                                    result.NativeCode = method.NativeCode;
                                    if (method.Name == "MoveNext")
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (TryGetContinuation(obj, out ClrObject continuation))
                    {
                        AddContinuation(continuation, result.Continuations);
                    }

                    found.Add(obj, result);
                }

                // Mark off objects that are referenced by others and thus aren't top level
                foreach (KeyValuePair<ClrObject, AsyncObject> entry in found)
                {
                    foreach (ClrObject continuation in entry.Value.Continuations)
                    {
                        if (found.TryGetValue(continuation, out AsyncObject asyncContinuation))
                        {
                            asyncContinuation.TopLevel = false;
                        }
                    }
                }

                return found;
            }

            // <summary>Adds the continuation into the list of continuations.</summary>
            // <remarks>
            // If the continuation is actually a List{object}, enumerate the list to add
            // each of the individual continuations to the continuations list.
            // </remarks>
            void AddContinuation(ClrObject continuation, List<ClrObject> continuations)
            {
                if (continuation.Type is not null)
                {
                    if (continuation.Type.Name is not null &&
                        continuation.Type.Name.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal))
                    {
                        if (continuation.Type.GetFieldByName("_items") is ClrInstanceField itemsField)
                        {
                            ClrObject itemsObj = itemsField.ReadObject(continuation.Address, interior: false);
                            if (!itemsObj.IsNull)
                            {
                                ClrArray items = itemsObj.AsArray();
                                if (items.Rank == 1)
                                {
                                    for (int i = 0; i < items.Length; i++)
                                    {
                                        if (items.GetObjectValue(i) is ClrObject { IsValid: true } c)
                                        {
                                            continuations.Add(ResolveContinuation(c));
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        continuations.Add(continuation);
                    }
                }
            }

            // <summary>Tries to get the object contents of a Task's continuations field</summary>
            bool TryGetContinuation(ClrObject obj, out ClrObject continuation)
            {
                if (obj.Type is not null &&
                    obj.Type.GetFieldByName("m_continuationObject") is ClrInstanceField continuationObjectField &&
                    continuationObjectField.ReadObject(obj.Address, interior: false) is ClrObject { IsValid: true } continuationObject)
                {
                    continuation = ResolveContinuation(continuationObject);
                    return true;
                }

                continuation = default;
                return false;
            }

            // <summary>Analyzes a continuation object to try to follow to the actual continuation target.</summary>
            ClrObject ResolveContinuation(ClrObject continuation)
            {
                ClrObject tmp;

                // If the continuation is an async method box, there's nothing more to resolve.
                if (IsTask(continuation.Type) && IsStateMachineBox(continuation.Type))
                {
                    return continuation;
                }

                // If it's a standard task continuation, get its task field.
                if (TryGetValidObjectField(continuation, "m_task", out tmp))
                {
                    return tmp;
                }

                // If it's storing an action wrapper, try to follow to that action's target.
                if (TryGetValidObjectField(continuation, "m_action", out tmp))
                {
                    continuation = tmp;
                }

                // If we now have an Action, try to follow through to the delegate's target.
                if (TryGetValidObjectField(continuation, "_target", out tmp))
                {
                    continuation = tmp;

                    // In some cases, the delegate's target might be a ContinuationWrapper, in which case we want to unwrap that as well.
                    if (continuation.Type?.Name == "System.Runtime.CompilerServices.AsyncMethodBuilderCore+ContinuationWrapper" &&
                        TryGetValidObjectField(continuation, "_continuation", out tmp))
                    {
                        continuation = tmp;
                        if (TryGetValidObjectField(continuation, "_target", out tmp))
                        {
                            continuation = tmp;
                        }
                    }
                }

                // Use whatever we ended with.
                return continuation;
            }

            // <summary>Determines if a type is or is derived from Task.</summary>
            bool IsTask(ClrType? type)
            {
                while (type is not null)
                {
                    if (type.MetadataToken == taskType.MetadataToken &&
                        type.Module == taskType.Module)
                    {
                        return true;
                    }

                    type = type.BaseType;
                }

                return false;
            }
        }

        /// <summary>Writes out a header line.  If DML is supported, this will be bolded.</summary>
        private void WriteHeaderLine(string text)
        {
            if (Console.SupportsDml)
            {
                Console.WriteDml($"<b>{text}</b>{Environment.NewLine}");
            }
            else
            {
                WriteLine(text);
            }
        }

        /// <summary>Writes out a method table address.  If DML is supported, this will be linked.</summary>
        /// <param name="mt">The method table address.</param>
        /// <param name="asyncObject">
        /// true if this is an async-related object; otherwise, false.  If true and if DML is supported,
        /// a link to dumpasync will be generated.  If false and if DML is supported, a link to dumpmt
        /// will be generated.
        /// </param>
        private void WriteMethodTable(ulong mt, bool asyncObject)
        {
            string completed = IncludeCompleted ? "--completed" : "";
            string tasks = IncludeTasks ? "--tasks" : "";

            switch ((Console.SupportsDml, asyncObject, IntPtr.Size))
            {
                case (false, _, 4):
                    Console.Write($"{mt,16:x8}");
                    break;

                case (false, _, 8):
                    Console.Write($"{mt:x16}");
                    break;

                case (true, true, 4):
                    Console.WriteDml($"<exec cmd=\"{DmlCommandInvoke} --methodtable 0x{mt:x8} {tasks} {completed}\">{mt,16:x8}</exec>");
                    break;

                case (true, true, 8):
                    Console.WriteDml($"<exec cmd=\"{DmlCommandInvoke} --methodtable 0x{mt:x16} {tasks} {completed}\">{mt:x16}</exec>");
                    break;

                case (true, false, 4):
                    Console.WriteDml($"<exec cmd=\"!DumpMT /d 0x{mt:x8}\">{mt,16:x8}</exec>");
                    break;

                case (true, false, 8):
                    Console.WriteDml($"<exec cmd=\"!DumpMT /d 0x{mt:x16}\">{mt:x16}</exec>");
                    break;
            }
        }

        /// <summary>Writes out an object address.  If DML is supported, this will be linked.</summary>
        /// <param name="addr">The object address.</param>
        /// <param name="asyncObject">
        /// true if this is an async-related object; otherwise, false.  If true and if DML is supported,
        /// a link to dumpasync will be generated.  If false and if DML is supported, a link to dumpobj
        /// will be generated.
        /// </param>
        private void WriteAddress(ulong addr, bool asyncObject)
        {
            string completed = IncludeCompleted ? "--completed" : "";
            string tasks = IncludeTasks ? "--tasks" : "";

            switch ((Console.SupportsDml, asyncObject, IntPtr.Size))
            {
                case (false, _, 4):
                    Console.Write($"{addr,16:x8}");
                    break;

                case (false, _, 8):
                    Console.Write($"{addr:x16}");
                    break;

                case (true, true, 4):
                    Console.WriteDml($"<exec cmd=\"{DmlCommandInvoke} --address 0x{addr:x8} {tasks} {completed} --fields\">{addr,16:x8}</exec>");
                    break;

                case (true, true, 8):
                    Console.WriteDml($"<exec cmd=\"{DmlCommandInvoke} --address 0x{addr:x16} {tasks} {completed} --fields\">{addr:x16}</exec>");
                    break;

                case (true, false, 4):
                    Console.WriteDml($"<exec cmd=\"!DumpObj /d 0x{addr:x8}\">{addr,16:x8}</exec>");
                    break;

                case (true, false, 8):
                    Console.WriteDml($"<exec cmd=\"!DumpObj /d 0x{addr:x16}\">{addr:x16}</exec>");
                    break;
            }
        }

        /// <summary>Writes out a value type address.  If DML is supported, this will be linked.</summary>
        /// <param name="addr">The value type's address.</param>
        /// <param name="mt">The value type's method table address.</param>
        private void WriteValueTypeAddress(ulong addr, ulong mt)
        {
            switch ((Console.SupportsDml, IntPtr.Size))
            {
                case (false, 4):
                    Console.Write($"{addr,16:x8}");
                    break;

                case (false, 8):
                    Console.Write($"{addr:x16}");
                    break;

                case (true, 4):
                    Console.WriteDml($"<exec cmd=\"!DumpVC /d 0x{mt:x8} 0x{addr:x8}\">{addr,16:x8}</exec>");
                    break;

                case (true, 8):
                    Console.WriteDml($"<exec cmd=\"!DumpVC /d 0x{mt:x16} 0x{addr:x16}\">{addr:x16}</exec>");
                    break;
            }
        }

        /// <summary>Writes out a link that should open the source code for an address, if available.</summary>
        /// <remarks>If DML is not supported, this is a nop.</remarks>
        private void WriteCodeLink(ulong address)
        {
            if (address != 0 && address != ulong.MaxValue &&
                Console.SupportsDml)
            {
                Console.WriteDml($" <link cmd=\".open -a 0x{address:x}\" alt=\"Source link\">@ {address:x}</link>");
            }
        }

        /// <summary>Gets whether the specified type is an AsyncStateMachineBox{T}.</summary>
        private static bool IsStateMachineBox(ClrType? type)
        {
            // Ideally we would compare the metadata token and module for the generic template for the type,
            // but that information isn't fully available via ClrMd, nor can it currently find DebugFinalizableAsyncStateMachineBox
            // due to various limitations.  So we're left with string comparison.
            const string Prefix = "System.Runtime.CompilerServices.AsyncTaskMethodBuilder<";
            return
                type?.Name is string name &&
                name.StartsWith(Prefix, StringComparison.Ordinal) &&
                name.IndexOf("AsyncStateMachineBox", Prefix.Length, StringComparison.Ordinal) >= 0;
        }

        /// <summary>Tries to get the compiler-generated state machine instance from a state machine box.</summary>
        private static bool TryGetStateMachine(ClrObject obj, out IClrValue? stateMachine)
        {
            // AsyncStateMachineBox<T> has a StateMachine field storing the compiler-generated instance.
            if (obj.Type?.GetFieldByName("StateMachine") is ClrInstanceField field)
            {
                if (field.IsValueType)
                {
                    if (obj.ReadValueTypeField("StateMachine") is ClrValueType { IsValid: true } t)
                    {
                        stateMachine = t;
                        return true;
                    }
                }
                else if (field.ReadObject(obj.Address, interior: false) is ClrObject { IsValid: true } t)
                {
                    stateMachine = t;
                    return true;
                }
            }

            stateMachine = null;
            return false;
        }

        /// <summary>Extract from the specified field of the specified object something that can be ToString'd.</summary>
        private static object GetDisplay(IClrValue obj, ClrInstanceField field)
        {
            if (field.Name is string fieldName)
            {
                switch (field.ElementType)
                {
                    case ClrElementType.Boolean:
                        return obj.ReadField<bool>(fieldName) ? "true" : "false";

                    case ClrElementType.Char:
                        char c = obj.ReadField<char>(fieldName);
                        return c >= 32 && c < 127 ? $"'{c}'" : $"'\\u{(int)c:X4}'";

                    case ClrElementType.Int8:
                        return obj.ReadField<sbyte>(fieldName);

                    case ClrElementType.UInt8:
                        return obj.ReadField<byte>(fieldName);

                    case ClrElementType.Int16:
                        return obj.ReadField<short>(fieldName);

                    case ClrElementType.UInt16:
                        return obj.ReadField<ushort>(fieldName);

                    case ClrElementType.Int32:
                        return obj.ReadField<int>(fieldName);

                    case ClrElementType.UInt32:
                        return obj.ReadField<uint>(fieldName);

                    case ClrElementType.Int64:
                        return obj.ReadField<long>(fieldName);

                    case ClrElementType.UInt64:
                        return obj.ReadField<ulong>(fieldName);

                    case ClrElementType.Float:
                        return obj.ReadField<float>(fieldName);

                    case ClrElementType.Double:
                        return obj.ReadField<double>(fieldName);

                    case ClrElementType.String:
                        return $"\"{obj.ReadStringField(fieldName)}\"";

                    case ClrElementType.Pointer:
                    case ClrElementType.NativeInt:
                    case ClrElementType.NativeUInt:
                    case ClrElementType.FunctionPointer:
                        return obj.ReadField<ulong>(fieldName).ToString(IntPtr.Size == 8 ? "x16" : "x8");

                    case ClrElementType.SZArray:
                        IClrValue arrayObj = obj.ReadObjectField(fieldName);
                        if (!arrayObj.IsNull)
                        {
                            ClrArray arrayObjAsArray = arrayObj.AsArray();
                            return $"{arrayObj.Type?.ComponentType?.ToString() ?? "unknown"}[{arrayObjAsArray.Length}]";
                        }
                        return "null";

                    case ClrElementType.Struct:
                        return field.GetAddress(obj.Address).ToString(IntPtr.Size == 8 ? "x16" : "x8");

                    case ClrElementType.Array:
                    case ClrElementType.Object:
                    case ClrElementType.Class:
                        IClrValue classObj = obj.ReadObjectField(fieldName);
                        return classObj.IsNull ? "null" : classObj.Address.ToString(IntPtr.Size == 8 ? "x16" : "x8");

                    case ClrElementType.Var:
                        return "(var)";

                    case ClrElementType.GenericInstantiation:
                        return "(generic instantiation)";

                    case ClrElementType.MVar:
                        return "(mvar)";

                    case ClrElementType.Void:
                        return "(void)";
                }
            }

            return "(unknown)";
        }

        /// <summary>Tries to get a ClrMethod for the method wrapped by a delegate object.</summary>
        private static bool TryGetMethodFromDelegate(ClrRuntime runtime, ClrObject delegateObject, out ClrMethod? method)
        {
            ClrInstanceField? methodPtrField = delegateObject.Type?.GetFieldByName("_methodPtr");
            ClrInstanceField? methodPtrAuxField = delegateObject.Type?.GetFieldByName("_methodPtrAux");

            if (methodPtrField is not null && methodPtrAuxField is not null)
            {
                ulong methodPtr = methodPtrField.Read<UIntPtr>(delegateObject.Address, interior: false).ToUInt64();
                if (methodPtr != 0)
                {
                    method = runtime.GetMethodByInstructionPointer(methodPtr);
                    if (method is null)
                    {
                        methodPtr = methodPtrAuxField.Read<UIntPtr>(delegateObject.Address, interior: false).ToUInt64();
                        if (methodPtr != 0)
                        {
                            method = runtime.GetMethodByInstructionPointer(methodPtr);
                        }
                    }

                    return method is not null;
                }
            }

            method = null;
            return false;
        }

        /// <summary>Creates an indenting string.</summary>
        /// <param name="count">The number of tabs.</param>
        private static string Tabs(int count) => new string(' ', count * TabWidth);

        /// <summary>Shortens a string to a maximum length by eliding part of the string with ...</summary>
        private static string? Truncate(string? value, int maxLength)
        {
            if (value is not null && value.Length > maxLength)
            {
                value = $"...{value.Substring(value.Length - maxLength + 3)}";
            }

            return value;
        }

        /// <summary>Tries to get the state flags from a task.</summary>
        private static bool TryGetTaskStateFlags(ClrObject obj, out int flags)
        {
            if (obj.Type?.GetFieldByName("m_stateFlags") is ClrInstanceField field)
            {
                flags = field.Read<int>(obj.Address, interior: false);
                return true;
            }

            flags = 0;
            return false;
        }

        /// <summary>Tries to read the specified value from the field of an entity.</summary>
        private static bool TryRead<T>(IClrValue entity, string fieldName, out T result) where T : unmanaged
        {
            if (entity.Type?.GetFieldByName(fieldName) is not null)
            {
                result = entity.ReadField<T>(fieldName);
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>Tries to read an object from a field of another object.</summary>
        private static bool TryGetValidObjectField(ClrObject obj, string fieldName, out ClrObject result)
        {
            if (obj.Type?.GetFieldByName(fieldName) is ClrInstanceField field &&
                field.ReadObject(obj.Address, interior: false) is { IsValid: true } validObject)
            {
                result = validObject;
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>Gets whether a task has completed, based on its state flags.</summary>
        private static bool IsCompleted(int taskStateFlags)
        {
            const int TASK_STATE_COMPLETED_MASK = 0x1600000;
            return (taskStateFlags & TASK_STATE_COMPLETED_MASK) != 0;
        }

        /// <summary>Determines whether a span contains all zeros.</summary>
        private static bool AllZero(ReadOnlySpan<byte> bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Gets a string representing interesting aspects of the specified task state flags.</summary>
        /// <remarks>
        /// The goal of this method isn't to detail every flag value (there are a lot).
        /// Rather, we only want to render flags that are likely to be valuable, e.g.
        /// we don't render WaitingForActivation, as that's the expected state for any
        /// task that's showing up in a stack.
        /// </remarks>
        private static string DescribeTaskFlags(int stateFlags)
        {
            if (stateFlags != 0)
            {
                StringBuilder? sb = null;
                void Append(string s)
                {
                    sb ??= new StringBuilder();
                    if (sb.Length != 0) sb.Append("|");
                    sb.Append(s);
                }

                if ((stateFlags & 0x10000) != 0) Append("Started");
                if ((stateFlags & 0x20000) != 0) Append("DelegateInvoked");
                if ((stateFlags & 0x40000) != 0) Append("Disposed");
                if ((stateFlags & 0x80000) != 0) Append("ExceptionObservedByParent");
                if ((stateFlags & 0x100000) != 0) Append("CancellationAcknowledged");
                if ((stateFlags & 0x200000) != 0) Append("Faulted");
                if ((stateFlags & 0x400000) != 0) Append("Canceled");
                if ((stateFlags & 0x800000) != 0) Append("WaitingOnChildren");
                if ((stateFlags & 0x1000000) != 0) Append("RanToCompletion");
                if ((stateFlags & 0x4000000) != 0) Append("CompletionReserved");

                if (sb is not null)
                {
                    return sb.ToString();
                }
            }

            return " ";
        }

        /// <summary>Gets detailed help for the command.</summary>
        protected override string GetDetailedHelp() => s_detailedHelpText;

        /// <summary>Represents an async object to be used as a frame in an async "stack".</summary>
        private sealed class AsyncObject
        {
            /// <summary>The actual object on the heap.</summary>
            public ClrObject Object;
            /// <summary>true if <see cref="Object"/> is an AsyncStateMachineBox.</summary>
            public bool IsStateMachine;
            /// <summary>A compiler-generated state machine extracted from the object, if one exists.</summary>
            public IClrValue? StateMachine;
            /// <summary>The state of the state machine, if the object contains a state machine.</summary>
            public int AwaitState;
            /// <summary>The <see cref="Object"/>'s Task state flags, if it's a task.</summary>
            public int TaskStateFlags;
            /// <summary>Whether this object meets the user-specified criteria for inclusion.</summary>
            public bool IncludeInOutput;
            /// <summary>true if this is a top-level instance that nothing else continues to.</summary>
            /// <remarks>This starts off as true and then is flipped to false when we find a continuation to this object.</remarks>
            public bool TopLevel = true;
            /// <summary>The address of the native code for a method on the object (typically MoveNext for a state machine).</summary>
            public ulong NativeCode;
            /// <summary>This object's continuations.</summary>
            public readonly List<ClrObject> Continuations = new();
        }
    }
}
