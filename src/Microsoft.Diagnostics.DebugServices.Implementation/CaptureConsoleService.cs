// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public sealed class CaptureConsoleService : IConsoleService
    {
        private readonly CharToLineConverter _charToLineConverter;
        private readonly List<string> _builder = new();

        public CaptureConsoleService() => _charToLineConverter = new((line) => _builder.Add(line));

        public void Clear() => _builder.Clear();

        public IReadOnlyList<string> OutputLines => _builder;

        public override string ToString() => string.Concat(_builder);

        #region IConsoleService

        public void Write(string text) => _charToLineConverter.Input(text);

        public void WriteWarning(string text) => _charToLineConverter.Input(text);

        public void WriteError(string text) => _charToLineConverter.Input(text);

        public bool SupportsDml => false;

        public void WriteDml(string text) => throw new NotSupportedException();

        public void WriteDmlExec(string text, string _) => throw new NotSupportedException();

        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

        int IConsoleService.WindowWidth => int.MaxValue;

        #endregion
    }
}
