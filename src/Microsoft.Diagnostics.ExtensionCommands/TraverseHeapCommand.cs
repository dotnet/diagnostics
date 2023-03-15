// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "traverseheap", Aliases = new[] { "TraverseHeap" }, Help = "Writes out heap information to a file in a format understood by the CLR Profiler.")]
    public class TraverseHeapCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public RootCacheService RootCache { get; set; }

        [Option(Name = "-xml")]
        public bool Xml { get; set; }

        [Argument(Name = "filename")]
        public string Filename { get; set; }

        public override void ExtensionInvoke()
        {
            if (string.IsNullOrWhiteSpace(Filename))
            {
                throw new ArgumentException($"Output filename cannot be empty.", nameof(Filename));
            }

            // create file early in case it throws
            using StreamWriter output = File.CreateText(Filename);
            using (XmlWriter xml = Xml ? XmlWriter.Create(output, new XmlWriterSettings() { Encoding = new UTF8Encoding(true), Indent = true, OmitXmlDeclaration = true }) : null)
            {
                using StreamWriter text = Xml ? null : output;

                // must be called first to initialize types
                (MemoryStream rootObjectStream, Dictionary<ClrType, int> types) = WriteRootsAndObjects();

                xml?.WriteStartElement("gcheap");
                xml?.WriteStartElement("types");

                foreach (KeyValuePair<ClrType, int> kv in types.OrderBy(kv => kv.Value))
                {
                    string name = kv.Key?.Name ?? $"error-reading-type-name:{kv.Key.MethodTable:x}";
                    int typeId = kv.Value;

                    xml?.WriteStartElement("type");
                    xml?.WriteAttributeString("id", typeId.ToString());
                    xml?.WriteAttributeString("name", name);
                    xml?.WriteEndElement();

                    text?.WriteLine($"t {typeId} 0 {name}");
                }
                xml?.WriteEndElement();

                xml?.Flush();
                text?.Flush();

                output.WriteLine();
                output.Flush();

                rootObjectStream.Position = 0;
                rootObjectStream.CopyTo(output.BaseStream);

                xml?.WriteEndElement();
            }
        }

        private (MemoryStream Stream, Dictionary<ClrType, int> Types) WriteRootsAndObjects()
        {
            Dictionary<ClrType, int> types = new();
            MemoryStream rootObjectStream = new();

            using StreamWriter text = Xml ? null : new StreamWriter(rootObjectStream, Encoding.Default, 4096, leaveOpen: true);
            using XmlWriter xml = Xml ? XmlWriter.Create(rootObjectStream, new XmlWriterSettings()
            {
                Encoding = new UTF8Encoding(false),
                CloseOutput = false,
                Indent = true,
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Fragment
            }) : null;

            int currObj = 1;
            int currType = 1;

            xml?.WriteStartElement("roots");
            text?.Write("r");
            foreach (ClrRoot root in RootCache.EnumerateRoots())
            {
                string kind = root switch
                {
                    ClrStackRoot => "stack",
                    ClrHandle => "handle",
                    _ => "finalizer"
                };

                xml?.WriteStartElement("root");
                xml?.WriteAttributeString("kind", kind);
                xml?.WriteAttributeString("address", FormatHex(root.Address));
                xml?.WriteEndElement();

                text?.Write(" ");
                text?.Write(FormatHex(root.Address));
            }
            xml?.WriteEndElement();
            text?.WriteLine();

            xml?.WriteStartElement("objects");
            foreach (ClrObject obj in Runtime.Heap.EnumerateObjects())
            {
                if (!obj.IsValid)
                {
                    continue;
                }

                ulong size = obj.Size;
                int objId = currObj++;
                if (!types.TryGetValue(obj.Type, out int typeId))
                {
                    typeId = types[obj.Type] = currType++;
                }

                xml?.WriteStartElement("object");
                xml?.WriteAttributeString("address", FormatHex(obj.Address));
                xml?.WriteAttributeString("typeid", typeId.ToString());
                xml?.WriteAttributeString("size", size.ToString());

                text?.WriteLine($"n {objId} 1 {typeId} {size}");
                text?.WriteLine($"! 1 {FormatHex(obj.Address)} {objId}");

                text?.Write($"o {FormatHex(obj.Address)} {typeId} {size} "); // trailing space intentional

                if (obj.ContainsPointers)
                {
                    foreach (ClrObject objRef in obj.EnumerateReferences(considerDependantHandles: true))
                    {
                        xml?.WriteStartElement("member");
                        xml?.WriteAttributeString("address", FormatHex(objRef.Address));
                        xml?.WriteEndElement();

                        text?.Write($" ");
                        text?.Write(FormatHex(objRef.Address));
                    }
                }

                text?.WriteLine();
                xml?.WriteEndElement();
            }
            xml?.WriteEndElement();

            return (rootObjectStream, types);
        }

        private static string FormatHex(ulong address) => $"0x{address:x16}";
    }
}
