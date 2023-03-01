// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ParallelStacks.Runtime
{
    public abstract class RendererBase : IRenderer
    {
        private readonly int _limit;

        protected RendererBase(int limit)
        {
            _limit = limit;
        }

        public int DisplayThreadIDsCountLimit => _limit;

        public abstract void Write(string text);
        public abstract void WriteCount(string count);
        public abstract void WriteNamespace(string ns);
        public abstract void WriteType(string type);
        public abstract void WriteSeparator(string separator);
        public abstract void WriteDark(string separator);
        public abstract void WriteMethod(string method);
        public abstract void WriteMethodType(string type);
        public abstract void WriteFrameSeparator(string text);
        public abstract string FormatTheadId(uint threadID);
    }
}
