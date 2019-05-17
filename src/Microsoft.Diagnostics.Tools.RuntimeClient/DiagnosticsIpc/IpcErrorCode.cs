using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.DiagnosticsIpc
{
    internal enum DiagnosticServerErrorCode : uint
    {
        OK = 0x00000000,
        BadEncoding = 0x00000001,
        UnknownCommandSet = 0x00000002,
        UnknownCommandId = 0x00000003,
        UnknownVersion = 0x00000004,
        // future

        BAD = 0xFFFFFFFF,
    }
}
