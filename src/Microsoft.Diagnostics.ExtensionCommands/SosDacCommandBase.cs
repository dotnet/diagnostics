// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using SOS.Hosting.DbgEng.Interop;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using SOS.Hosting.Interop;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public abstract class SosDacCommandBase : CommandBase
    {
        private readonly RuntimeWrapper _runtime;
        private ISOSDacInterface _dac;

        private protected SosDacCommandBase(IServiceProvider services, RuntimeWrapper runtime)
            : base(services)
        {
            _runtime = runtime;
        }

        [FilterInvoke(Message = ClrRuntimeCommandBase.RuntimeNotFoundMessage)]
        public static bool FilterInvoke(IServiceProvider services) => services.GetService<RuntimeWrapper>() != null;

        private protected ISOSDacInterface Dac => _dac ??= GetDac();

        private protected bool WarnIfMinidumpSafeMode()
        {
            INativeDebuggerDumpService settings = Services.GetService<INativeDebuggerDumpService>();
            INativeDebugControl2 control = Services.GetService<INativeDebugControl2>();
            if (settings?.IsMinidumpSafeModeEnabled() != 0 ||
                control is null ||
                control.GetDebuggeeType(out _, out DEBUG_CLASS_QUALIFIER qualifier) < 0 ||
                qualifier != DEBUG_CLASS_QUALIFIER.USER_WINDOWS_SMALL_DUMP ||
                control.GetDumpFormatFlags(out DEBUG_FORMAT flags) < 0 ||
                (flags & DEBUG_FORMAT.USER_SMALL_FULL_MEMORY) != 0)
            {
                return false;
            }

            Console.WriteLine("This command is not supported in a minidump without full memory");
            Console.WriteLine("To try the command anyway, run !MinidumpMode 0");
            return true;
        }

        private protected bool UseDml(HostType hostType, bool requested) => hostType == HostType.DbgEng && (requested || Console.SupportsDml);

        private protected bool TryParseAddressOrExpression(string text, out ulong address)
        {
            return TryParseAddress(text, out address) ||
                (!string.IsNullOrWhiteSpace(text) && Services.GetService<INativeDebuggerExpressionService>()?.EvaluateExpression(text, out address) == 0);
        }

        private ISOSDacInterface GetDac()
        {
            int hr = _runtime.GetSosDac(out ISOSDacInterface dac);
            if (hr < 0)
            {
                throw new CommandNotFoundException(new[] { $"Unable to acquire ISOSDacInterface (0x{hr:X8})." });
            }
            return dac;
        }
    }
}
