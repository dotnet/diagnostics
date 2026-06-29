// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    /// <summary>
    /// An <see cref="IConsoleService"/> that writes to an arbitrary <see cref="TextWriter"/>.
    /// This lets <see cref="Table"/> render into a file (for example !dumplog's output file)
    /// instead of the debugger console. DML is not supported.
    /// </summary>
    internal sealed class TextWriterConsole : IConsoleService
    {
        private readonly TextWriter _writer;

        public TextWriterConsole(TextWriter writer, CancellationToken cancellationToken)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            CancellationToken = cancellationToken;
        }

        public void Write(string value) => _writer.Write(value);

        public void WriteWarning(string value) => _writer.Write(value);

        public void WriteError(string value) => _writer.Write(value);

        public bool SupportsDml => false;

        public void WriteDml(string text) => throw new NotSupportedException();

        public void WriteDmlExec(string text, string action) => throw new NotSupportedException();

        public CancellationToken CancellationToken { get; set; }

        public int WindowWidth => int.MaxValue;
    }
}
