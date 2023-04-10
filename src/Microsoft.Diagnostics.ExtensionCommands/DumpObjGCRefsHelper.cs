// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.TableOutput;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpobjgcrefs", Help = "A helper command to implement !dumpobj -refs")]
    public sealed class DumpObjGCRefsHelper : CommandBase
    {
        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [Argument(Name = "object")]
        public string ObjectAddress { get; set; }

        public override void Invoke()
        {
            if (!TryParseAddress(ObjectAddress, out ulong objAddress))
            {
                throw new ArgumentException($"Invalid object address: '{ObjectAddress}'", nameof(ObjectAddress));
            }

            ClrObject obj = Runtime.Heap.GetObject(objAddress);
            if (!obj.IsValid)
            {
                Console.WriteLine($"Unable to walk object references, invalid object.");
                return;
            }

            ClrReference[] refs = obj.EnumerateReferencesWithFields(carefully: false, considerDependantHandles: false).ToArray();
            if (refs.Length == 0 )
            {
                Console.WriteLine("GC Refs: none");
                return;
            }

            int fieldNameLen = Math.Max(refs.Max(r => GetFieldName(r)?.Length ?? 0), 5);
            int offsetLen = Math.Max(refs.Max(r => r.Offset.ToSignedHexString().Length), 6);

            TableOutput output = new(Console, (fieldNameLen, ""), (offsetLen, ""), (16, "x12"));
            output.WriteRow("Field", "Offset", "Object", "Type");
            foreach (ClrReference objRef in refs)
            {
                output.WriteRow(GetFieldName(objRef), objRef.Offset, new DmlDumpObj(objRef.Object), objRef.Object.Type?.Name);
            }
        }

        private static string GetFieldName(ClrReference objRef)
        {
            if (objRef.Field is null)
            {
                return null;
            }

            if (objRef.InnerField is null)
            {
                return objRef.Field?.Name;
            }

            StringBuilder sb = new(260);
            bool foundOneFieldName = false;

            for (ClrReference? curr = objRef; curr.HasValue; curr = curr.Value.InnerField)
            {
                if (sb.Length > 0)
                {
                    sb.Append('.');
                }

                string fieldName = curr.Value.Field?.Name;
                if (string.IsNullOrWhiteSpace(fieldName))
                {
                    sb.Append("???");
                }
                else
                {
                    sb.Append(fieldName);
                    foundOneFieldName = true;
                }
            }

            // Make sure we don't just return "???.???.???"
            return foundOneFieldName ? sb.ToString() : null;
        }
    }
}
