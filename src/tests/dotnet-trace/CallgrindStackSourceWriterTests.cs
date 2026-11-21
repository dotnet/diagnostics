// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Diagnostics.Tracing.Stacks;
using Xunit;

namespace Microsoft.Diagnostics.Tools.Trace
{
    public class CallgrindStackSourceWriterTests
    {
        [Fact]
        public void Export_WritesExpectedCosts()
        {
            TestStackSource source = new();
            source.AddSample(1, "Thread (1)", "app!A", "app!B");
            source.AddSample(2, "Thread (1)", "app!A", "app!B", "app!A", "app!B");
            source.AddSample(3, "Thread (1)", "app!A", "lib!C", "CPU_TIME");
            source.AddSample(4, "Thread (1)");

            StringWriter writer = new();
            CallgrindStackSourceWriter.Export(source, writer, "trace.nettrace");

            string expected = """
                # callgrind format
                version: 1
                creator: dotnet-trace
                cmd: trace.nettrace

                positions: line
                event: Time : Time (us)
                events: Time
                summary: 6000

                ob=(1) app
                fl=(1) app
                fn=(1) B
                0 1000
                cob=(1)
                cfi=(1)
                cfn=(2) A'2
                calls=1 0
                0 2000

                ob=(1)
                fl=(1)
                fn=(3) A
                0 0
                cob=(1)
                cfi=(1)
                cfn=(1)
                calls=2 0
                0 3000
                cob=(2) lib
                cfi=(2) lib
                cfn=(4) C
                calls=1 0
                0 3000

                ob=(1)
                fl=(1)
                fn=(5) B'2
                0 2000

                ob=(1)
                fl=(1)
                fn=(2)
                0 0
                cob=(1)
                cfi=(1)
                cfn=(5)
                calls=1 0
                0 2000

                ob=(2)
                fl=(2)
                fn=(4)
                0 3000
                """;

            Assert.Equal(expected.ReplaceLineEndings(), writer.ToString().TrimEnd());
        }

        [Fact]
        public void Export_CollapsesAdjacentUnresolvedFrames()
        {
            TestStackSource source = new();
            source.AddSample(1, "Thread (1)", "?!?", "?!?", "?!?", "app!Main", "?!?", "?!?");

            StringWriter writer = new();
            CallgrindStackSourceWriter.Export(source, writer);
            string output = writer.ToString();

            Assert.Contains("fn=(1) ?'2" + Environment.NewLine, output);
            Assert.DoesNotContain("?'3", output);
        }

        private sealed class TestStackSource : StackSource
        {
            private readonly List<string> _frames = new();
            private readonly List<(StackSourceFrameIndex Frame, StackSourceCallStackIndex Caller)> _stacks = new();
            private readonly List<(StackSourceCallStackIndex Stack, float Metric)> _samples = new();

            public void AddSample(float metric, params string[] rootToLeaf)
            {
                StackSourceCallStackIndex stack = StackSourceCallStackIndex.Invalid;
                foreach (string frameName in rootToLeaf)
                {
                    int frame = _frames.IndexOf(frameName);
                    if (frame < 0)
                    {
                        frame = _frames.Count;
                        _frames.Add(frameName);
                    }

                    _stacks.Add(((StackSourceFrameIndex)(frame + (int)StackSourceFrameIndex.Start), stack));
                    stack = (StackSourceCallStackIndex)(_stacks.Count - 1);
                }

                _samples.Add((stack, metric));
            }

            public override void ForEach(Action<StackSourceSample> callback)
            {
                StackSourceSample sample = new(this);
                foreach ((StackSourceCallStackIndex stack, float metric) in _samples)
                {
                    sample.StackIndex = stack;
                    sample.Metric = metric;
                    callback(sample);
                }
            }

            public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex) => _stacks[(int)callStackIndex].Caller;

            public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex) => _stacks[(int)callStackIndex].Frame;

            public override string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName) => _frames[frameIndex - StackSourceFrameIndex.Start];

            public override int CallStackIndexLimit => _stacks.Count;

            public override int CallFrameIndexLimit => _frames.Count + (int)StackSourceFrameIndex.Start;
        }
    }
}
