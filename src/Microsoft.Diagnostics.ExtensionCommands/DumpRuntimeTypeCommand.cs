// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.TableOutput;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpruntimetypes", Help = "Finds all System.RuntimeType objects in the GC heap and prints the type name and MethodTable they refer too.")]
    public sealed class DumpRuntimeTypeCommand : CommandBase
    {
        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        public override void Invoke()
        {
            TableOutput output = null;

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
                string typeName;
                bool isMethodTable = (m_handle & 2) == 0;
                if (isMethodTable)
                {
                    // Only lookup the type if we have a MethodTable.
                    ClrType type = Runtime.GetTypeByMethodTable(m_handle);
                    typeName = type?.Name ?? $"methodtable: {m_handle:x}";
                    domain = type?.Module?.AppDomain;
                }
                else
                {
                    typeName = $"typehandle: {m_handle:x} (SOS does not support resolving typehandle names.)";
                }

                if (output is null)
                {
                    output = new(Console, (16, "x12"), (16, "x12"), (16, "x12"));
                    output.WriteRow("Address", "Domain", "MT", "Type Name");
                }

                output.WriteRow(new DmlDumpObj(runtimeType.Address),
                                domain is not null ? new DmlDumpDomain(domain.Address) : null,
                                isMethodTable ? new DmlDumpMT(m_handle) : m_handle,
                                typeName);
            }

            if (output is null)
            {
                Console.WriteLine("No System.RuntimeType objects found.");
            }
        }
    }
}
