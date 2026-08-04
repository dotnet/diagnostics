// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Adapter exposing the host's <see cref="IModuleService"/> /
    /// <see cref="IModuleSymbols"/> through ClrMD's
    /// <see cref="IClrSymbolProvider"/> contract: symbol name/address resolution
    /// plus type field-offset lookups (backed by the debugger's type system).
    /// </summary>
    [ServiceExport(Type = typeof(IClrSymbolProvider), Scope = ServiceScope.Target)]
    public sealed class HostSymbolProvider : IClrSymbolProvider
    {
        private readonly IModuleService _moduleService;
        private readonly ulong _signExtensionMask;

        public HostSymbolProvider(IModuleService moduleService, IMemoryService memoryService)
        {
            _moduleService = moduleService ?? throw new ArgumentNullException(nameof(moduleService));
            _signExtensionMask = memoryService?.SignExtensionMask() ?? ulong.MaxValue;
        }

        public bool TryGetSymbolName(ulong address, out string symbolName, out ulong displacement)
        {
            symbolName = null;
            displacement = 0;

            address &= _signExtensionMask;

            IModule module;
            try
            {
                module = _moduleService.GetModuleFromAddress(address);
            }
            catch (DiagnosticsException)
            {
                return false;
            }
            if (module is null)
            {
                return false;
            }

            IModuleSymbols symbols = module.Services.GetService<IModuleSymbols>();
            if (symbols is null)
            {
                return false;
            }

            if (!symbols.TryGetSymbolName(address, out string bareName, out displacement)
                || string.IsNullOrEmpty(bareName))
            {
                return false;
            }

            // Strip any module! qualifier the lower-level service might have
            // prepended — the new contract returns bare names only.
            int bang = bareName.IndexOf('!');
            symbolName = bang >= 0 && bang + 1 < bareName.Length ? bareName.Substring(bang + 1) : bareName;
            return true;
        }

        public bool TryGetSymbolAddress(ulong moduleBase, string name, out ulong address)
        {
            address = 0;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            moduleBase &= _signExtensionMask;

            IModule scopedModule;
            try
            {
                scopedModule = _moduleService.GetModuleFromBaseAddress(moduleBase);
            }
            catch (DiagnosticsException)
            {
                return false;
            }
            if (scopedModule is null)
            {
                return false;
            }

            IModuleSymbols scopedSymbols = scopedModule.Services.GetService<IModuleSymbols>();
            if (scopedSymbols is null)
            {
                return false;
            }

            if (scopedSymbols.TryGetSymbolAddress(name, out ulong scopedAddr) && scopedAddr != 0)
            {
                address = scopedAddr;
                return true;
            }
            return false;
        }

        public bool TryGetFieldOffset(ulong moduleBase, string typeName, string fieldName, out uint offset)
        {
            offset = 0;
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            moduleBase &= _signExtensionMask;

            IModule scopedModule;
            try
            {
                scopedModule = _moduleService.GetModuleFromBaseAddress(moduleBase);
            }
            catch (DiagnosticsException)
            {
                return false;
            }
            if (scopedModule is null)
            {
                return false;
            }

            IModuleSymbols scopedSymbols = scopedModule.Services.GetService<IModuleSymbols>();
            if (scopedSymbols is null)
            {
                return false;
            }

            if (!scopedSymbols.TryGetType(typeName, out IType type) || type is null)
            {
                return false;
            }

            if (!type.TryGetField(fieldName, out IField field) || field is null)
            {
                return false;
            }

            offset = field.Offset;
            return true;
        }
    }
}
