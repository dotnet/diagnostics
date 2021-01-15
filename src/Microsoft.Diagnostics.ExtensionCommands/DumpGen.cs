﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System.Collections.Generic;
using System.Linq;
using System;

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
            var types = new Dictionary<ClrType, DumpGenStats>();

            foreach (var obj in _helper.EnumerateObjectsInGeneration(_generation)
                .Where(obj => typeNameFilter == null || IsTypeNameMatching(obj.Type.Name, typeNameFilter)))
            {
                var objectType = obj.Type;
                if (types.TryGetValue(objectType, out var type))
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
            return typeName.ToLower().Contains(typeNameFilter.ToLower());
        }

    }

}
