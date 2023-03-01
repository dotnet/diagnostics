// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace ParallelStacks.Runtime
{
    public static class RendererHelpers
    {
        public static void Render(this ParallelStack stacks, IRenderer visitor)
        {
            RenderStack(stacks, visitor);
        }

        private const int Padding = 5;
        private static void RenderStack(ParallelStack stack, IRenderer visitor, int increment = 0)
        {
            string alignment = new string(' ', Padding * increment);
            if (stack.Stacks.Count == 0)
            {
                StackFrame lastFrame = stack.Frame;
                visitor.Write($"{Environment.NewLine}{alignment}");
                visitor.WriteFrameSeparator($" ~~~~ {FormatThreadIdList(visitor, stack.ThreadIds)}");
                visitor.WriteCount($"{Environment.NewLine}{alignment}{stack.ThreadIds.Count,Padding} ");

                RenderFrame(lastFrame, visitor);
                return;
            }

            foreach (ParallelStack nextStackFrame in stack.Stacks.OrderBy(s => s.ThreadIds.Count))
            {
                RenderStack(nextStackFrame, visitor,
                    (nextStackFrame.ThreadIds.Count == stack.ThreadIds.Count) ? increment : increment + 1);
            }

            StackFrame currentFrame = stack.Frame;
            visitor.WriteCount($"{Environment.NewLine}{alignment}{stack.ThreadIds.Count,Padding} ");
            RenderFrame(currentFrame, visitor);
        }

        private static string FormatThreadIdList(IRenderer visitor, List<uint> threadIds)
        {
            int count = threadIds.Count;
            int limit = visitor.DisplayThreadIDsCountLimit;
            limit = Math.Min(count, limit);
            if (limit < 0)
            {
                return string.Join(",", threadIds.Select(tid => visitor.FormatTheadId(tid)));
            }
            else
            {
                string result = string.Join(",", threadIds.GetRange(0, limit).Select(tid => visitor.FormatTheadId(tid)));
                if (count > limit)
                {
                    result += "...";
                }

                return result;
            }
        }

        private static void RenderFrame(StackFrame frame, IRenderer visitor)
        {
            if (!string.IsNullOrEmpty(frame.TypeName))
            {
                string[] namespaces = frame.TypeName.Split('.');
                for (int i = 0; i < namespaces.Length - 1; i++)
                {
                    visitor.WriteNamespace(namespaces[i]);
                    visitor.WriteSeparator(".");
                }
                visitor.WriteMethodType(namespaces[namespaces.Length - 1]);
                visitor.WriteSeparator(".");
            }

            visitor.WriteMethod(frame.MethodName);
            visitor.WriteSeparator("(");

            List<string> parameters = frame.Signature;
            for (int current = 0; current < parameters.Count; current++)
            {
                string parameter = parameters[current];
                // handle byref case
                int pos = parameter.LastIndexOf(" ByRef", StringComparison.InvariantCulture);
                if (pos != -1)
                {
                    visitor.WriteType(parameter.Substring(0, pos));
                    visitor.WriteDark(" ByRef");
                }
                else
                {
                    visitor.WriteType(parameter);
                }
                if (current < parameters.Count - 1)
                {
                    visitor.WriteSeparator(", ");
                }
            }
            visitor.WriteSeparator(")");
        }


    }
}
