// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    /// <summary>
    /// Exports a StackSource to the callgrind format used by KCachegrind/QCachegrind.
    /// Format reference: https://valgrind.org/docs/manual/cl-format.html
    /// </summary>
    internal static class CallgrindStackSourceWriter
    {
        public static void WriteStackViewAsCallgrind(StackSource source, string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            using (StreamWriter writer = File.CreateText(filePath))
            {
                Export(source, writer);
            }
        }

        private static void Export(StackSource source, TextWriter writer)
        {
            Dictionary<string, FunctionCosts> functions = new();

            source.ForEach(sample =>
            {
                // Walk the stack from leaf to root to collect all frames.
                List<string> frames = new();
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

                // frames is leaf-to-root; reverse to root-to-leaf
                frames.Reverse();

                // Skip Thread, Process, and other pseudo-frames at the top of the stack
                int startIndex = 0;
                for (int i = 0; i < frames.Count; i++)
                {
                    string frame = frames[i];
                    if (frame.StartsWith("Thread (") || frame.StartsWith("Process")
                        || frame == "Threads" || frame == "(Non-Activities)")
                    {
                        startIndex = i + 1;
                    }
                    else
                    {
                        break;
                    }
                }

                if (startIndex >= frames.Count)
                {
                    return;
                }

                // The leaf frame gets self cost
                string leafFrame = frames[frames.Count - 1];
                FunctionCosts leafCosts = GetOrCreateFunction(functions, leafFrame);
                leafCosts.SelfCost += sample.Metric;

                // Record caller->callee relationships
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

            // Write callgrind header
            writer.WriteLine("# callgrind format");
            writer.WriteLine("version: 1");
            writer.WriteLine("creator: dotnet-trace");
            writer.WriteLine("cmd: unknown");
            writer.WriteLine();
            writer.WriteLine("positions: line");
            writer.WriteLine("events: Time");
            writer.WriteLine();

            // Write function entries
            foreach (KeyValuePair<string, FunctionCosts> entry in functions)
            {
                ParseFrameName(entry.Key, out string module, out string method);
                FunctionCosts costs = entry.Value;

                writer.WriteLine("fl=" + module);
                writer.WriteLine("fn=" + method);

                // Self cost (converted from ms to microseconds as integer)
                long selfCostUs = (long)(costs.SelfCost * 1000.0);
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "0 {0}", selfCostUs));

                // Call edges
                foreach (KeyValuePair<string, CallEdge> callee in costs.Callees)
                {
                    ParseFrameName(callee.Key, out string calleeModule, out string calleeMethod);
                    CallEdge edge = callee.Value;

                    long inclusiveCostUs = (long)(edge.InclusiveCost * 1000.0);

                    writer.WriteLine("cfl=" + calleeModule);
                    writer.WriteLine("cfn=" + calleeMethod);
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "calls={0} 0", edge.Count));
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "0 {0}", inclusiveCostUs));
                }

                writer.WriteLine();
            }
        }

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
                module = "unknown";
                method = frameName;
            }
        }

        private static FunctionCosts GetOrCreateFunction(Dictionary<string, FunctionCosts> functions, string name)
        {
            if (!functions.TryGetValue(name, out FunctionCosts costs))
            {
                costs = new();
                functions[name] = costs;
            }

            return costs;
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
