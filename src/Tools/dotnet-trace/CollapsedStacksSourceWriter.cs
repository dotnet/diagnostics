// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace Microsoft.Diagnostics.Tools.Trace
{
	static class CollapsedStacksSourceWriter
	{
		internal static void Write(StackSource stackSource, string outputFilename)
		{
			var dict = new Dictionary<string, float>();
			stackSource.ForEach(sample => {
				var stack = new List<string>();
				var stackIndex = sample.StackIndex;
				var metric = sample.Metric;
				while (stackIndex != StackSourceCallStackIndex.Invalid)
                {
                    var frameName = stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), false);
                    if (frameName.StartsWith("Thread ("))
						break;
					stack.Add(frameName);
					
					stackIndex = stackSource.GetCallerIndex(stackIndex);
                }
				stack.Reverse();
				var stackStr = string.Join(";", stack);
				if (dict.TryGetValue(stackStr, out var currentValue))
					dict[stackStr] = currentValue + metric;
				else
					dict[stackStr] = metric;
			});
			var result = dict.ToArray();
			if (result.Length == 0)
				Console.WriteLine("Warning: No stacks collected.");
			// sort for deterministic output
			Array.Sort(result, (a, b) => a.Key.CompareTo(b.Key));
			using (var writeStream = File.CreateText(outputFilename))
				foreach (var stack in result)
				{
					writeStream.Write(stack.Key);
					writeStream.Write(" ");
					writeStream.WriteLine((int)stack.Value);
				}
		}
	}
}
