// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Xml.Linq;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Extensions
{
    internal sealed class ConsoleServiceFromDebuggerServices : IConsoleService
    {
        private readonly DebuggerServices _debuggerServices;
        private bool? _supportsDml;

        public ConsoleServiceFromDebuggerServices(DebuggerServices debuggerServices)
        {
            _debuggerServices = debuggerServices;
        }

        #region IConsoleService

        public void Write(string text) => _debuggerServices.OutputString(DEBUG_OUTPUT.NORMAL, text);

        public void WriteWarning(string text) => _debuggerServices.OutputString(DEBUG_OUTPUT.WARNING, text);

        public void WriteError(string text) => _debuggerServices.OutputString(DEBUG_OUTPUT.ERROR, text);

        public void WriteDml(string text) => _debuggerServices.OutputDmlString(DEBUG_OUTPUT.NORMAL, text);

        public void WriteDmlExec(string text, string cmd)
        {
            if (!SupportsDml || string.IsNullOrWhiteSpace(cmd))
            {
                Write(text);
            }
            else
            {
                string dml = $"<exec cmd=\"{DmlEscape(cmd)}\">{DmlEscape(text)}</exec>";
                WriteDml(dml);
            }
        }

        public bool SupportsDml => _supportsDml ??= _debuggerServices.SupportsDml;

        public CancellationToken CancellationToken { get; set; }

        int IConsoleService.WindowWidth => _debuggerServices.GetOutputWidth();

        #endregion

        private static string DmlEscape(string text) => string.IsNullOrWhiteSpace(text) ? text : new XText(text).ToString();
    }
}
