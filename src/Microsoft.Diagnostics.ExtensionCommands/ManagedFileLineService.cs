﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [ServiceExport(Scope = ServiceScope.Runtime)]
    public class ManagedFileLineService
    {
        private readonly Dictionary<ClrModule, ISymbolFile> _cache = new();

        [ServiceImport]
        public ISymbolService SymbolService { get; set; }

        [ServiceImport]
        public IModuleService ModuleService { get; set; }

        public (string Source, int Line) GetSourceFromManagedMethod(ClrMethod method, int nativeOffset)
        {
            ClrModule clrModule = method?.Type?.Module;
            if (clrModule is null)
            {
                return default;
            }

            int ilOffset = GetILOffsetForNativeOffset(method, nativeOffset >= 0 ? (uint)nativeOffset : 0);
            ISymbolFile symbols = GetSymbolForClrModule(clrModule);
            if (symbols is null)
            {
                return default;
            }

            symbols.GetSourceLineByILOffset(method.MetadataToken, ilOffset, out int lineNumber, out string fileName);
            return (fileName, lineNumber);
        }

        private ISymbolFile GetSymbolForClrModule(ClrModule clrModule)
        {
            if (_cache.TryGetValue(clrModule, out ISymbolFile symbolFile))
            {
                return symbolFile;
            }

            IModule module = ModuleService.GetModuleFromBaseAddress(clrModule.ImageBase);
            if (module is null)
            {
                return null;
            }

            string path = SymbolService.DownloadSymbolFile(module);
            if (path is null || !File.Exists(path))
            {
                return null;
            }
            try
            {
                symbolFile = SymbolService.OpenSymbolFile(File.OpenRead(path));
                _cache.Add(clrModule, symbolFile);
                return symbolFile;
            }
            catch (IOException)
            {
                _cache.Add(clrModule, null);
                // path could be locked by anti-virus, or deleted between when checked and when we try to open it
                return null;
            }
        }

        private static int GetILOffsetForNativeOffset(ClrMethod method, uint nativeOffset)
        {
            ImmutableArray<ILToNativeMap> ilmap = method.ILOffsetMap;
            if (ilmap.IsDefaultOrEmpty)
            {
                return -1;
            }

            (ulong Distance, int Offset) closest = (ulong.MaxValue, -1);
            foreach (ILToNativeMap entry in ilmap)
            {
                ulong distance = GetDistance(entry, nativeOffset);
                if (distance == 0)
                {
                    return entry.ILOffset;
                }

                if (distance < closest.Distance)
                {
                    closest = (distance, entry.ILOffset);
                }
            }

            return closest.Offset;
        }

        private static ulong GetDistance(ILToNativeMap entry, uint nativeOffset)
        {
            ulong distance = 0;
            if (nativeOffset < entry.StartAddress)
            {
                distance = entry.StartAddress - nativeOffset;
            }
            else if (nativeOffset > entry.EndAddress)
            {
                distance = nativeOffset - entry.EndAddress;
            }

            return distance;
        }
    }
}