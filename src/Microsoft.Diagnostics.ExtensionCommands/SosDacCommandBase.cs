// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
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
