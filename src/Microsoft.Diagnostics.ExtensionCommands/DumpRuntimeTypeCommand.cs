// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpruntimetypes", Aliases = new[] { "DumpRuntimeTypes" }, Help = "Finds all System.RuntimeType objects in the GC heap and prints the type name and MethodTable they refer to.")]
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

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"DumpRuntimeTypes finds all System.RuntimeType objects in the gc heap and 
prints the type name and MethodTable they refer too. Sample output:

     Address   Domain       MT Type Name
    ------------------------------------------------------------------------------
      a515f4   14a740 5baf8d28 System.TypedReference
      a51608   14a740 5bb05764 System.Globalization.BaseInfoTable
      a51958   14a740 5bb05b24 System.Globalization.CultureInfo
      a51a44   14a740 5bb06298 System.Globalization.GlobalizationAssembly
      a51de0   14a740 5bb069c8 System.Globalization.TextInfo
      a56b98   14a740 5bb12d28 System.Security.Permissions.HostProtectionResource
      a56bbc   14a740 5baf7248 System.Int32
      a56bd0   14a740 5baf3fdc System.String
      a56cfc   14a740 5baf36a4 System.ValueType
    ...

This command will print a ""?"" in the domain column if the type is loaded into multiple
AppDomains.  For example:

    {prompt}dumpruntimetypes
     Address   Domain       MT Type Name              
    ------------------------------------------------------------------------------
     28435a0        ?   3f6a8c System.TypedReference
     28435b4        ?   214d6c System.ValueType
     28435c8        ?   216314 System.Enum
     28435dc        ?   2147cc System.Object
     284365c        ?   3cd57c System.IntPtr
     2843670        ?   3feaac System.Byte
     2843684        ?  23a544c System.IEquatable`1[[System.IntPtr, mscorlib]]
     2843784        ?   3c999c System.Int32
     2843798        ?   3caa04 System.IEquatable`1[[System.Int32, mscorlib]]
";
    }
}
