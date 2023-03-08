// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public class DumpGen
    {
        private readonly ClrMDHelper _helper;
        private readonly GCGeneration _generation;

        public DumpGen(ClrMDHelper helper, GCGeneration generation)
        {
            _helper = helper;
            _generation = generation;
        }

        public IEnumerable<DumpGenStats> GetStats(string typeNameFilter)
        {
            Dictionary<ClrType, DumpGenStats> types = new();

            foreach (ClrObject obj in _helper.EnumerateObjectsInGeneration(_generation)
                .Where(obj => typeNameFilter == null || IsTypeNameMatching(obj.Type.Name, typeNameFilter)))
            {
                ClrType objectType = obj.Type;
                if (types.TryGetValue(objectType, out DumpGenStats type))
                {
                    type.NumberOfOccurences++;
                    type.TotalSize += obj.Size;
                }
                else
                {
                    types.Add(objectType, new DumpGenStats { Type = objectType, NumberOfOccurences = 1, TotalSize = obj.Size });
                }
            }
            return types.Values.OrderBy(v => v.TotalSize);
        }

        public IEnumerable<ClrObject> GetInstances(ulong methodTableAddress)
        {
            return _helper.EnumerateObjectsInGeneration(_generation)
                .Where(obj => obj.Type.MethodTable == methodTableAddress);
        }

        private static bool IsTypeNameMatching(string typeName, string typeNameFilter)
        {
            return typeName != null && typeName.IndexOf(typeNameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
