// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Tools.Common
{
    /// <summary>
    /// Abstraction over console operations so tools can render to custom consoles in tests.
    /// Mirrors the APIs dotnet-counters and dotnet-trace need for their renderers.
    /// </summary>
    internal interface IConsole
    {
        int WindowHeight { get; }
        int WindowWidth { get; }
        bool CursorVisible { get; set; }
        int CursorTop { get; }
        int BufferWidth { get; }

        void Clear();
        void SetCursorPosition(int col, int row);
        void Write(string text);
        void WriteLine(string text);
        void WriteLine();
    }
}
