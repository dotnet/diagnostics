// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using ParallelStacks.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public class MonoColorConsoleRenderer : RendererBase
    {
        private readonly IConsoleService _console;

        public MonoColorConsoleRenderer(IConsoleService console, int limit = -1) : base(limit)
        {
            _console = console;
        }

        public override void Write(string text)
        {
            Output(text);
        }

        public override void WriteCount(string count)
        {
            Output(count);
        }

        public override void WriteNamespace(string ns)
        {
            Output(ns);
        }

        public override void WriteType(string type)
        {
            Output(type);
        }

        public override void WriteSeparator(string separator)
        {
            Output(separator);
        }

        public override void WriteDark(string separator)
        {
            Output(separator);
        }

        public override void WriteMethod(string method)
        {
            Output(method);
        }

        public override void WriteMethodType(string type)
        {
            Output(type);
        }

        public override void WriteFrameSeparator(string text)
        {
            Output(text);
        }

        public override string FormatTheadId(uint threadID)
        {
            var idInHex = threadID.ToString("x");
            return idInHex;
        }

        private void Output(string text)
        {
            _console.Write(text);
        }
    }
}
