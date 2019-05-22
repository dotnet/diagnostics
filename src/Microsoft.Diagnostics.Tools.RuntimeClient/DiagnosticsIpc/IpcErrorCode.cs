using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.DiagnosticsIpc
{
    internal enum DiagnosticServerErrorCode : uint
    {
        OK              = 0x00000000,
        BadEncoding     = 0x00000001,
        UnknownCommand  = 0x00000002,
        UnknownMagic    = 0x00000003,
        BadInput        = 0x00000004,

        ServerError     = 0x00000005,
        // future

        BAD             = 0xFFFFFFFF,
    }
}
