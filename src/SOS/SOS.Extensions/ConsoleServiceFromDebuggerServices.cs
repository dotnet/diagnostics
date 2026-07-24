// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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

        public bool SupportsDml => _supportsDml ??= _debuggerServices.SupportsDml;

        public CancellationToken CancellationToken { get; set; }

        int IConsoleService.WindowWidth => _debuggerServices.GetOutputWidth();

        void IConsoleService.WriteString(OutputType type, OutputLevel level, string text)
        {
            switch (type)
            {
                case OutputType.Default:
                    switch (level)
                    {
                        case OutputLevel.Normal:
                            _debuggerServices.OutputString(DEBUG_OUTPUT.NORMAL, text);
                            break;
                        case OutputLevel.Warning:
                            _debuggerServices.OutputString(DEBUG_OUTPUT.WARNING, text);
                            break;
                        case OutputLevel.Error:
                            _debuggerServices.OutputString(DEBUG_OUTPUT.ERROR, text);
                            break;
                        case OutputLevel.Verbose:
                            _debuggerServices.OutputString(DEBUG_OUTPUT.VERBOSE, text);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(level), level, null);
                        }
                    break;
                case OutputType.Dml:
                    _debuggerServices.OutputDmlString(DEBUG_OUTPUT.NORMAL, text);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        #endregion

        private static string DmlEscape(string text) => string.IsNullOrWhiteSpace(text) ? text : new XText(text).ToString();
    }
}
