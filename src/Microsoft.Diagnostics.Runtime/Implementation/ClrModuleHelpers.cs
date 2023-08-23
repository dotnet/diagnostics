// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime.DacInterface;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class ClrModuleHelpers : IClrModuleHelpers
    {
        private const int mdtTypeDef = 0x02000000;
        private const int mdtTypeRef = 0x01000000;
        private readonly SOSDac _sos;
        private readonly IClrAppDomainHelpers _appDomainHelpers;

        public IDataReader DataReader { get; }

        public ClrModuleHelpers(SOSDac sos, IDataReader dataReader, IClrAppDomainHelpers appDomainHelpers)
        {
            _sos = sos;
            DataReader = dataReader;
            _appDomainHelpers = appDomainHelpers;
        }

        public IClrNativeHeapHelpers GetNativeHeapHelpers() => _appDomainHelpers.GetNativeHeapHelpers();

        public ClrExtendedModuleData GetExtendedData(ClrModule module)
        {
            using ClrDataModule? dataModule = _sos.GetClrDataModule(module.Address);
            if (dataModule is null || !dataModule.GetModuleData(out ExtendedModuleData data))
                return new();

            return new()
            {
                IsFlatLayout = data.IsFlatLayout != 0,
                IsDynamic = data.IsDynamic != 0,
                Size = data.LoadedPESize,
                SimpleName = dataModule.GetName(),
                FileName = dataModule.GetFileName()
            };
        }

        public IEnumerable<(ulong MethodTable, int Token)> EnumerateTypeDefMap(ClrModule module) => GetModuleMap(module, SOSDac.ModuleMapTraverseKind.TypeDefToMethodTable);

        public IEnumerable<(ulong MethodTable, int Token)> EnumerateTypeRefMap(ClrModule module) => GetModuleMap(module, SOSDac.ModuleMapTraverseKind.TypeRefToMethodTable);

        private List<(ulong MethodTable, int Token)> GetModuleMap(ClrModule module, SOSDac.ModuleMapTraverseKind kind)
        {
            int tokenType = kind == SOSDac.ModuleMapTraverseKind.TypeDefToMethodTable ? mdtTypeDef : mdtTypeRef;
            List<(ulong MethodTable, int Token)> result = new();
            _sos.TraverseModuleMap(kind, module.Address, (token, mt, _) => {
                result.Add((mt, token | tokenType));
            });

            return result;
        }

        public MetadataImport? GetMetadataImport(ClrModule module) => _sos.GetMetadataImport(module.Address);

        public string? GetAssemblyName(ClrModule module) => _sos.GetAssemblyName(module.AssemblyAddress);
    }
}