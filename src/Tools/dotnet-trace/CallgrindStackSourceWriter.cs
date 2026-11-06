// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    /// <summary>
    /// Exports a StackSource to the callgrind format used by KCachegrind/QCachegrind and callgrind_annotate.
    /// Format reference: https://valgrind.org/docs/manual/cl-format.html
    /// </summary>
    internal static class CallgrindStackSourceWriter
    {
        private const string UnknownModule = "???";
        private const string UnresolvedFrame = "?!?";

        public static void WriteStackViewAsCallgrind(StackSource source, string filePath, string command = null)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            using (StreamWriter writer = File.CreateText(filePath))
            {
                Export(source, writer, command);
            }
        }

        internal static void Export(StackSource source, TextWriter writer, string command = null)
        {
            Dictionary<string, FunctionCosts> functions = new();
            List<string> frames = new();
            Dictionary<string, int> recursionDepth = new();
            double totalCost = 0;

            source.ForEach(sample =>
            {
                frames.Clear();
                StackSourceCallStackIndex stackIndex = sample.StackIndex;
                while (stackIndex != StackSourceCallStackIndex.Invalid)
                {
                    StackSourceFrameIndex frameIndex = source.GetFrameIndex(stackIndex);
                    if (frameIndex != StackSourceFrameIndex.Broken && frameIndex != StackSourceFrameIndex.Invalid)
                    {
                        string name = source.GetFrameName(frameIndex, false);
                        if (!string.IsNullOrEmpty(name))
                        {
                            frames.Add(name);
                        }
                    }

                    stackIndex = source.GetCallerIndex(stackIndex);
                }

                // The thread time computer marks running samples with a CPU_TIME leaf; drop it so that
                // the self cost is attributed to the method that was executing.
                if (frames.Count > 0 && frames[0] == "CPU_TIME")
                {
                    frames.RemoveAt(0);
                }

                // frames is leaf-to-root; reverse to root-to-leaf
                frames.Reverse();

                // Skip Thread, Process, and other pseudo-frames at the top of the stack
                int startIndex = 0;
                while (startIndex < frames.Count && IsPseudoFrame(frames[startIndex]))
                {
                    startIndex++;
                }

                if (startIndex >= frames.Count)
                {
                    return;
                }

                // Runs of unresolved frames are distinct native frames, not recursion; collapse them into one.
                for (int i = frames.Count - 1; i > startIndex; i--)
                {
                    if (frames[i] == UnresolvedFrame && frames[i - 1] == UnresolvedFrame)
                    {
                        frames.RemoveAt(i);
                    }
                }

                // Like callgrind's --separate-recs, the Nth recursive activation of a function is named "name'N".
                // Tools compute inclusive cost from incoming call costs, so without this a recursive call would be
                // counted on top of the outer call that already contains it.
                recursionDepth.Clear();
                for (int i = startIndex; i < frames.Count; i++)
                {
                    recursionDepth.TryGetValue(frames[i], out int depth);
                    recursionDepth[frames[i]] = ++depth;
                    if (depth > 1)
                    {
                        frames[i] = string.Format(CultureInfo.InvariantCulture, "{0}'{1}", frames[i], depth);
                    }
                }

                totalCost += sample.Metric;
                GetOrCreateFunction(functions, frames[frames.Count - 1]).SelfCost += sample.Metric;

                for (int i = startIndex; i < frames.Count - 1; i++)
                {
                    string callerName = frames[i];
                    string calleeName = frames[i + 1];

                    FunctionCosts callerCosts = GetOrCreateFunction(functions, callerName);
                    if (!callerCosts.Callees.TryGetValue(calleeName, out CallEdge edge))
                    {
                        edge = new();
                        callerCosts.Callees[calleeName] = edge;
                    }

                    edge.Count++;
                    edge.InclusiveCost += sample.Metric;
                }
            });

            writer.WriteLine("# callgrind format");
            writer.WriteLine("version: 1");
            writer.WriteLine("creator: dotnet-trace");
            if (!string.IsNullOrEmpty(command))
            {
                writer.WriteLine("cmd: " + Sanitize(command));
            }
            writer.WriteLine();
            writer.WriteLine("positions: line");
            writer.WriteLine("event: Time : Time (us)");
            writer.WriteLine("events: Time");
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "summary: {0}", ToMicroseconds(totalCost)));
            writer.WriteLine();

            // Managed frames have no source file information, so the module name is used for both
            // the object (ob/cob) and the file (fl/cfi). callgrind_annotate requires a file per function.
            NameCompressor objects = new();
            NameCompressor files = new();
            NameCompressor functionNames = new();

            foreach (KeyValuePair<string, FunctionCosts> entry in functions)
            {
                ParseFrameName(entry.Key, out string module, out string method);
                FunctionCosts costs = entry.Value;

                writer.WriteLine("ob=" + objects.Get(module));
                writer.WriteLine("fl=" + files.Get(module));
                writer.WriteLine("fn=" + functionNames.Get(method));
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "0 {0}", ToMicroseconds(costs.SelfCost)));

                foreach (KeyValuePair<string, CallEdge> callee in costs.Callees)
                {
                    ParseFrameName(callee.Key, out string calleeModule, out string calleeMethod);
                    CallEdge edge = callee.Value;

                    writer.WriteLine("cob=" + objects.Get(calleeModule));
                    writer.WriteLine("cfi=" + files.Get(calleeModule));
                    writer.WriteLine("cfn=" + functionNames.Get(calleeMethod));
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "calls={0} 0", edge.Count));
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "0 {0}", ToMicroseconds(edge.InclusiveCost)));
                }

                writer.WriteLine();
            }
        }

        private static bool IsPseudoFrame(string frame) =>
            frame.StartsWith("Thread (", StringComparison.Ordinal)
            || frame.StartsWith("Process", StringComparison.Ordinal)
            || frame == "Threads"
            || frame == "(Non-Activities)";

        // StackSource metrics produced by the thread time computer are in milliseconds.
        private static long ToMicroseconds(double milliseconds) => (long)Math.Round(milliseconds * 1000.0);

        private static void ParseFrameName(string frameName, out string module, out string method)
        {
            int bangIndex = frameName.IndexOf('!');
            if (bangIndex > 0)
            {
                module = frameName.Substring(0, bangIndex);
                method = frameName.Substring(bangIndex + 1);
            }
            else
            {
                module = UnknownModule;
                method = frameName;
            }

            if (method.Length == 0)
            {
                method = UnknownModule;
            }
        }

        private static string Sanitize(string value) => value.Replace('\r', ' ').Replace('\n', ' ');

        private static FunctionCosts GetOrCreateFunction(Dictionary<string, FunctionCosts> functions, string name)
        {
            if (!functions.TryGetValue(name, out FunctionCosts costs))
            {
                costs = new();
                functions[name] = costs;
            }

            return costs;
        }

        /// <summary>
        /// Implements callgrind name compression: the first occurrence of a name is written as
        /// "(id) name" and later occurrences as "(id)".
        /// </summary>
        private sealed class NameCompressor
        {
            private readonly Dictionary<string, int> _ids = new();

            public string Get(string name)
            {
                if (_ids.TryGetValue(name, out int id))
                {
                    return string.Format(CultureInfo.InvariantCulture, "({0})", id);
                }

                id = _ids.Count + 1;
                _ids[name] = id;
                return string.Format(CultureInfo.InvariantCulture, "({0}) {1}", id, Sanitize(name));
            }
        }

        private sealed class FunctionCosts
        {
            public double SelfCost;
            public readonly Dictionary<string, CallEdge> Callees = new();
        }

        private sealed class CallEdge
        {
            public int Count;
            public double InclusiveCost;
        }
    }
}
