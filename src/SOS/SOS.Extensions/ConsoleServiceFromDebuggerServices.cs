using System.Threading;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Extensions
{
    internal class ConsoleServiceFromDebuggerServices : IConsoleService
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

        public bool SupportsDml => _supportsDml ??= _debuggerServices.SupportsDml;

        public CancellationToken CancellationToken { get; set; }

        int IConsoleService.WindowWidth => _debuggerServices.GetOutputWidth();

        #endregion
    }
}
