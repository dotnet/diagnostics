// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.FileFormats;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    internal static class HostLayouts
    {
        private static readonly ConditionalWeakTable<LayoutManager, object> s_registeredLayouts = new();

        internal static void Register(LayoutManager layoutManager)
        {
            lock (s_registeredLayouts)
            {
                if (!s_registeredLayouts.TryGetValue(layoutManager, out _))
                {
                    layoutManager
                        .RegisterArray<byte>()
                        .RegisterTStruct<SpecialDiagInfoHeader>()
                        .RegisterTStruct<ClrEngineMetrics>();
                    s_registeredLayouts.Add(layoutManager, new object());
                }
            }
        }
    }
}
