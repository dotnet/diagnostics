// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpruntimetypes", Aliases = new[] { "DumpRuntimeTypes" }, Help = "Finds all System.RuntimeType objects in the GC heap and prints the type name and MethodTable they refer too.")]
    public sealed class DumpRuntimeTypeCommand : ClrRuntimeCommandBase
    {
        public override void Invoke()
        {
            Table output = null;

            foreach (ClrObject runtimeType in Runtime.Heap.EnumerateObjects())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (!runtimeType.IsValid || !runtimeType.IsRuntimeType)
                {
                    continue;
                }

                if (!runtimeType.TryReadField("m_handle", out nuint m_handle))
                {
                    continue;
                }

                ClrAppDomain domain = null;
                object typeName = m_handle;
                bool isMethodTable = (m_handle & 2) == 0;
                if (isMethodTable)
                {
                    // Only lookup the type if we have a MethodTable.
                    ClrType type = Runtime.GetTypeByMethodTable(m_handle);
                    if (type is not null)
                    {
                        typeName = type;
                        domain = type.Module?.AppDomain;
                    }
                }
                else
                {
                    typeName = $"typehandle: {m_handle:x} (SOS does not support resolving typehandle names.)";
                }

                if (output is null)
                {
                    output = new(Console, DumpObj, DumpDomain, DumpHeap, TypeName);
                    output.WriteHeader("Address", "Domain", "MT", "Type Name");
                }

                // We pass .Address here instead of the ClrObject because every type is a RuntimeType, we don't need
                // or want the alt-text.
                output.WriteRow(runtimeType.Address, domain, m_handle, typeName);
            }

            if (output is null)
            {
                Console.WriteLine("No System.RuntimeType objects found.");
            }
        }
    }
}
