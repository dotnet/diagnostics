// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.FileFormats;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public class ClrEngineMetrics : TStruct
    {
        public const string Symbol = "g_CLREngineMetrics";

        public readonly int Size;
        public readonly int DbiVersion;
        public readonly SizeT ContinueStartupEvent;

        public static bool TryRead(IServiceProvider services, ulong address, out ClrEngineMetrics metrics)
        {
            metrics = default;

            Reader reader = services.GetService<Reader>();
            if (reader is null)
            {
                return false;
            }

            try
            {
                metrics = reader.Read<ClrEngineMetrics>(address);
            }
            catch (Exception ex) when (ex is InvalidVirtualAddressException or BadInputFormatException)
            {
                return false;
            }

            return true;
        }
    }
}
