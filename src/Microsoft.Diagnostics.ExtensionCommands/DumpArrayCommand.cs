// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using static SOS.Hosting.Interop.ClrDataAddressHelpers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumparray", Aliases = new[] { "da" }, Help = "Displays details about a managed array.")]
    public sealed class DumpArrayCommand(IServiceProvider services, RuntimeWrapper runtime, IMemoryService memory, IModuleService modules, ITarget target) : SosDacCommandBase(services, runtime)
    {
        [Argument<string>(Name = "address", Help = "Array address in hexadecimal or a debugger expression.")]
        public string Address { get; set; }

        [Option<string>(Name = "-start", Help = "Start index for a single-dimensional array.")]
        public string Start { get; set; }

        [Option<string>(Name = "-length", Help = "Number of elements to display.")]
        public string Length { get; set; }

        [Option<bool>(Name = "-details", Help = "Display details for each element.")]
        public bool Details { get; set; }

        [Option<bool>(Name = "-nofields", Help = "Omit the field table from element details.")]
        public bool NoFields { get; set; }

        [Option<bool>(Name = "/d", Help = "Enable debugger markup links.")]
        public bool DmlEnabled { get; set; }

        public override unsafe void Invoke()
        {
            if (WarnIfMinidumpSafeMode())
            {
                return;
            }
            HostType hostType = target.Host.HostType;
            if (string.IsNullOrWhiteSpace(Address))
            {
                throw new DiagnosticsException("No array address was provided.");
            }
            if (!TryParseAddressOrExpression(Address, out ulong address) || address == 0)
            {
                throw new DiagnosticsException($"Invalid parameter {Address}{(hostType == HostType.DotnetDump ? "\n" : "")}");
            }

            int? start = null;
            if (Start is not null)
            {
                if (!int.TryParse(Start, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                {
                    throw new DiagnosticsException($"Invalid parameter {Start}");
                }
                start = index;
            }
            ulong length = ParseCount(Length, ulong.MaxValue);
            bool dml = UseDml(hostType, DmlEnabled);
            DacObjectRenderer renderer = new(Dac, Console, memory, modules, target.OperatingSystem, dml);
            DacpObjectData data = renderer.ReadObject(address, hostType, warnOnInvalidGc: false, out _);
            if (data.ObjectType != DacpObjectType.OBJ_ARRAY)
            {
                string prefix = hostType == HostType.DbgEng ? "!" : "";
                Console.WriteLine($"Not an array, please use {prefix}dumpobj instead");
                return;
            }
            if (data.dwRank != 1 && (length != ulong.MaxValue || start.GetValueOrDefault() != 0))
            {
                Console.WriteLine("For multi-dimension array, length and start index are supported");
                return;
            }
            // The CLR supports ranks 1 through 32. Do not allocate an unbounded buffer from DAC data.
            if (data.dwRank is 0 or > 32)
            {
                throw new DiagnosticsException("Invalid array rank");
            }

            int rank = (int)data.dwRank;
            Span<int> indices = stackalloc int[rank];
            Span<int> lowerBounds = stackalloc int[rank];
            Span<uint> bounds = stackalloc uint[rank];
            for (int i = 0; i < rank; i++)
            {
                if (!memory.ReadDword(ToTargetAddress(data.ArrayLowerBoundsPtr, memory.PointerSize) + (uint)i * sizeof(uint), out uint lower))
                {
                    Console.WriteLine("Failed to read lower bounds info from the array");
                    return;
                }
                lowerBounds[i] = unchecked((int)lower);
            }
            for (int i = 0; i < rank; i++)
            {
                if (!memory.ReadDword(ToTargetAddress(data.ArrayBoundsPtr, memory.PointerSize) + (uint)i * sizeof(uint), out bounds[i]))
                {
                    Console.WriteLine("Failed to read bounds info from the array");
                    return;
                }
            }
            lowerBounds.CopyTo(indices);
            ulong offset = 0;
            if (rank == 1 && start is int startIndex)
            {
                long startOffset = (long)startIndex - lowerBounds[0];
                if (startOffset < 0 || (ulong)startOffset > data.dwNumComponents)
                {
                    Console.WriteLine("Start index out of range");
                    return;
                }
                indices[0] = startIndex;
                offset = (ulong)startOffset;
            }
            if (NoFields && !Details)
            {
                Console.WriteLine("-nofields has no effect unless -details is specified");
            }
            Console.WriteLine($"Name:        {DacCommandHelpers.GetMethodTableName(Dac, data.ElementTypeHandle)}[{new string(',', rank - 1)}]");
            renderer.WriteAddress("MethodTable: ", data.MethodTable, "DumpMT");
            DacpMethodTableData mt = default;
            if (Dac.GetMethodTableData(data.MethodTable, &mt) >= 0)
            {
                renderer.WriteAddress("EEClass:     ", mt.klass, "DumpClass");
            }
            renderer.WriteSize(data.Size);
            Console.WriteLine($"Array:       Rank {rank}, Number of elements {data.dwNumComponents}, Type {DacObjectRenderer.GetElementTypeName(data.ElementType)}");
            renderer.WriteAddress("Element Methodtable: ", data.ElementTypeHandle, "DumpMT");

            ulong count = Math.Min(length, data.dwNumComponents - offset);
            bool valueType = DacObjectRenderer.IsElementValueType(data.ElementType);
            IndentedConsole detailConsole = new(Console);
            DacObjectRenderer details = new(Dac, detailConsole, memory, modules, target.OperatingSystem, dml, nested: true);
            ulong baseAddress = ToTargetAddress(data.ArrayDataPtr, memory.PointerSize);
            for (ulong i = 0; i < count; i++, offset++)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                if (data.dwComponentSize == 0 || offset > (ulong.MaxValue - baseAddress) / data.dwComponentSize)
                {
                    throw new DiagnosticsException("Invalid array element address");
                }
                ulong elementAddress = baseAddress + offset * data.dwComponentSize;
                ulong element = elementAddress;
                if (!valueType && !memory.ReadPointer(elementAddress, out element))
                {
                    Console.Write("Failed to read element at ");
                    WriteIndices(indices);
                    Console.WriteLine();
                }
                else
                {
                    WriteIndices(indices);
                    Console.Write(" ");
                    if (element == 0)
                    {
                        Console.WriteLine("null");
                    }
                    else
                    {
                        if (valueType)
                        {
                            renderer.WriteValueClassLink(data.ElementTypeHandle, element);
                        }
                        else
                        {
                            DacCommandHelpers.WriteDacAddressLink(Console, element, memory.PointerSize, "DumpObj", dml, forceDml: dml);
                        }
                        Console.WriteLine();
                        if (Details)
                        {
                            try
                            {
                                if (valueType)
                                {
                                    details.WriteValueClass(data.ElementTypeHandle, element, !NoFields);
                                }
                                else
                                {
                                    details.WriteObject(element, !NoFields, hostType);
                                }
                            }
                            catch (DiagnosticsException error)
                            {
                                detailConsole.WriteLine(error.Message.TrimEnd('\n'));
                            }
                        }
                    }
                }

                for (int dimension = rank - 1; dimension >= 0; dimension--)
                {
                    long next = (long)indices[dimension] + 1;
                    if (next < (long)lowerBounds[dimension] + bounds[dimension])
                    {
                        indices[dimension] = (int)next;
                        break;
                    }
                    indices[dimension] = lowerBounds[dimension];
                }
            }
        }

        private void WriteIndices(ReadOnlySpan<int> indices)
        {
            foreach (int index in indices)
            {
                Console.Write($"[{index}]");
            }
        }

        private static ulong ParseCount(string text, ulong defaultValue)
        {
            if (text is null)
            {
                return defaultValue;
            }
            if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value))
            {
                throw new DiagnosticsException($"Invalid parameter {text}");
            }
            return value;
        }

        private sealed class IndentedConsole(IConsoleService inner) : IConsoleService
        {
            private bool _atLineStart = true;
            public bool SupportsDml => inner.SupportsDml;
            public int WindowWidth => inner.WindowWidth;
            public System.Threading.CancellationToken CancellationToken
            {
                get => inner.CancellationToken;
                set => inner.CancellationToken = value;
            }

            public void WriteString(OutputType type, OutputLevel level, string text)
            {
                for (int start = 0; start < text.Length;)
                {
                    int newline = text.IndexOf('\n', start);
                    int end = newline < 0 ? text.Length : newline + 1;
                    if (_atLineStart)
                    {
                        inner.WriteString(OutputType.Default, level, "    ");
                    }
                    inner.WriteString(type, level, text[start..end]);
                    _atLineStart = newline >= 0;
                    start = end;
                }
            }
        }
    }
}
