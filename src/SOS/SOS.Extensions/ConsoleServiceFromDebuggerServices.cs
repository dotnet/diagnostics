// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Interop;
using System.Threading;

namespace SOS.Extensions
{
    internal class ConsoleServiceFromDebuggerServices : IConsoleService
    {
        private readonly DebuggerServices _debuggerServices;

        public ConsoleServiceFromDebuggerServices(DebuggerServices debuggerServices)
        {
            _debuggerServices = debuggerServices;
        }

        #region IConsoleService

        public void Write(string text) => _debuggerServices.OutputString(DEBUG_OUTPUT.NORMAL, text);

        public void WriteWarning(string text) => _debuggerServices.OutputString(DEBUG_OUTPUT.WARNING, text);

        public void WriteError(string text) => _debuggerServices.OutputString(DEBUG_OUTPUT.ERROR, text);

        public CancellationToken CancellationToken { get; set; }

        #endregion
    }
}
