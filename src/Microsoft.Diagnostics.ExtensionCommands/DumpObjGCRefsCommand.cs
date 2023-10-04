// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpobjgcrefs", Help = "A helper command to implement !dumpobj -refs")]
    public sealed class DumpObjGCRefsCommand : ClrRuntimeCommandBase
    {
        private readonly StringBuilderPool _stringBuilderPool = new(260);

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
            if (refs.Length == 0)
            {
                Console.WriteLine("GC Refs: none");
                return;
            }

            Console.WriteLine("GC Refs:");

            Column fieldNameColumn = ColumnKind.Text.GetAppropriateWidth(refs.Select(r => GetFieldName(r)));
            Column offsetName = ColumnKind.HexOffset.GetAppropriateWidth(refs.Select(r => r.Offset));

            Table output = new(Console, fieldNameColumn, offsetName, ColumnKind.DumpObj, ColumnKind.TypeName);
            output.WriteHeader("Field", "Offset", "Object", "Type");
            foreach (ClrReference objRef in refs)
            {
                output.WriteRow(GetFieldName(objRef), objRef.Offset, objRef.Object, objRef.Object.Type);
            }
        }

        private string GetFieldName(ClrReference objRef)
        {
            Console.CancellationToken.ThrowIfCancellationRequested();

            if (objRef.Field is null)
            {
                return null;
            }

            if (objRef.InnerField is null)
            {
                return objRef.Field?.Name;
            }

            StringBuilder sb = _stringBuilderPool.Rent();
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
            string result = foundOneFieldName ? sb.ToString() : null;
            _stringBuilderPool.Return(sb);
            return result;
        }
    }
}
