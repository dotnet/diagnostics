// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    /// <summary>
    /// This interface abstracts the console writing code from the physical console
    /// and allows us to do unit testing. It is similar to the IConsole interface from System.CommandLine
    /// but unfortunately that one doesn't support all the APIs we use such as the size and positioning of
    /// the cursor.
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
