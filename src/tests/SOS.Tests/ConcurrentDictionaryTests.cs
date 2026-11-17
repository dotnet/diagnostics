// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text.RegularExpressions;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class ConcurrentDictionaryTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task Dcd_RejectsInvalidAddresses(TestConfig config)
    {
        SOSTestSkips.SkipUnsupportedDictionary(config);
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        target.Sos("dcd").AssertContains("Missing ConcurrentDictionary address");
        target.Sos("dcd abcdefgh").AssertContains("Hexadecimal address expected");
        target.Sos("dcd 0000000000000001").AssertContains("is not referencing an object");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task Dcd_PrimitiveKeysAndStringValues(TestConfig config)
    {
        SOSTestSkips.SkipUnsupportedDictionary(config);
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong dictionary = FindDictionary(target, 0);
        SosOutput output = target.Sos($"dcd {dictionary:x}");
        IReadOnlyDictionary<string, string> entries = ReadDictionary(output, "System.Int32, System.String", 3);
        Assert.Equal("\"one\"", entries["1"]);
        Assert.Equal("\"two\"", entries["2"]);
        Assert.Equal("\"three\"", entries["3"]);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task Dcd_ArrayValuesRoundTrip(TestConfig config)
    {
        SOSTestSkips.SkipUnsupportedDictionary(config);
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong dictionary = FindDictionary(target, 1);
        SosOutput output = target.Sos($"dcd {dictionary:x}");
        IReadOnlyDictionary<string, string> entries = ReadDictionary(output, "System.Int32, System.String[]", 2);
        AssertStringArray(target, entries["1"], 4);
        AssertStringArray(target, entries["2"], 2);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task Dcd_PrimitiveKeysAndValues(TestConfig config)
    {
        SOSTestSkips.SkipUnsupportedDictionary(config);
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong dictionary = FindDictionary(target, 2);
        SosOutput output = target.Sos($"dcd {dictionary:x}");
        IReadOnlyDictionary<string, string> entries = ReadDictionary(output, "System.Int32, System.Int32", 3);
        Assert.Equal("1", entries["0"]);
        Assert.Equal("17", entries["31"]);
        Assert.Equal("512487", entries["1521482"]);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task Dcd_StringKeysBooleanValuesAndTruncation(TestConfig config)
    {
        SOSTestSkips.SkipUnsupportedDictionary(config);
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong dictionary = FindDictionary(target, 3);
        SosOutput output = target.Sos($"dcd {dictionary:x}");
        IReadOnlyDictionary<string, string> entries = ReadDictionary(output, "System.String, System.Boolean", 3);
        Assert.Equal("True", entries["\"String true\""]);
        Assert.Equal("False", entries["\"String false\""]);
        Assert.Equal("False", entries["\"" + new string('S', 99) + "..."]);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task Dcd_StructKeysObjectAndNullValues(TestConfig config)
    {
        SOSTestSkips.SkipUnsupportedDictionary(config);
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong dictionary = FindDictionary(target, 4);
        SosOutput output = target.Sos($"dcd {dictionary:x}");
        IReadOnlyDictionary<string, string> entries = ReadDictionary(output, "DictionaryStructMarker, DictionaryClassMarker", 2);
        HashSet<int> keys = new();
        foreach (KeyValuePair<string, string> entry in entries)
        {
            DumpObjResult key = DumpStruct(target, entry.Key);
            int value = int.Parse(key.Field("IntValue").Value, CultureInfo.InvariantCulture);
            Assert.True(keys.Add(value), $"duplicate struct key {value}");
            Assert.Contains(value, new[] { 1, 2 });
            AssertStructFields(key, value);

            if (value == 2)
            {
                Assert.Equal("null", entry.Value);
            }
            else
            {
                ulong address = CommandAddress(entry.Value, "dumpobj");
                // https://github.com/dotnet/diagnostics/issues/5840: dumpobj can crash on Alpine.
                if (!RepoLayout.Rid.StartsWith("linux-musl-", StringComparison.Ordinal))
                {
                    DumpObjResult obj = target.DumpObj(address);
                    Assert.Equal("DictionaryClassMarker", obj.Name);
                    ObjFieldRow boolean = obj.Field("Value1");
                    Assert.Equal("instance", boolean.Attr);
                    Assert.Equal("System.Boolean", boolean.Type);
                    Assert.Equal("0", boolean.Value);
                    ObjFieldRow reference = obj.Field("Value2");
                    Assert.Equal("instance", reference.Attr);
                    Assert.Equal("System.String", reference.Type);
                    Assert.Equal(0ul, ObjectCommandParsing.Hex(reference.Value));
                }
            }
        }

        Assert.Equal([ 1, 2 ], keys.Order());
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task Dcd_StructValuesRoundTrip(TestConfig config)
    {
        SOSTestSkips.SkipUnsupportedDictionary(config);
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong dictionary = FindDictionary(target, 5);
        SosOutput output = target.Sos($"dcd {dictionary:x}");
        IReadOnlyDictionary<string, string> entries = ReadDictionary(output, "System.Int32, DictionaryStructMarker", 1);
        AssertStructFields(DumpStruct(target, entries["0"]), 12);
    }

    private static ulong FindDictionary(Target target, int index)
    {
        string module = TargetCatalog.Get(target.TargetName).ModuleFor(target.Flavor);
        EEMatch type = Assert.Single(target.Name2EE($"{module}!SosHarnessScenarios").Matches, match => match.Name == "SosHarnessScenarios");
        Assert.NotNull(type.MethodTable);
        DumpMtResult methodTable = target.DumpMt(type.MethodTable.Value);
        SosOutput fields = target.DumpClass(methodTable.EEClass ?? type.MethodTable.Value).Output;

        // Generic static fields can have a blank Type column, so match this exact root row instead of
        // treating every preceding row as a fully populated fields table. Runtime-owned dictionaries
        // may have the same generic type as our fixtures; the rooted array identifies our instances.
        Match root = Assert.Single(Regex.Matches(fields.Text,
            @"^[ \t]*.*[ \t]+static[ \t]+(?<address>(?:0x)?[0-9a-fA-F`]+)[ \t]+s_concurrentDictionaries[ \t]*\r?$", RegexOptions.Multiline).Cast<Match>());
        ulong address = ObjectCommandParsing.Hex(root.Groups["address"].Value);
        Assert.NotEqual(0ul, address);
        DumpArrayResult fixtures = target.DumpArray(address);
        Assert.Equal("System.Object[]", fixtures.Name);
        Assert.Equal(6, fixtures.NumberOfElements);
        Assert.Equal(6, fixtures.Elements.Count);
        ulong dictionary = Assert.Single(fixtures.Elements, element => element.Index == index).Address;
        Assert.NotEqual(0ul, dictionary);
        return dictionary;
    }

    private static IReadOnlyDictionary<string, string> ReadDictionary(SosOutput output, string typeArguments, int count)
    {
        output.AssertContains($"System.Collections.Concurrent.ConcurrentDictionary<{typeArguments}>");
        Assert.Matches($@"(?m)^{count} items\r?$", output.Text);
        Match[] matches = Regex.Matches(output.Text,
            @"^[ \t]*Key: (?<key>[^\r\n]+)\r?\n[ \t]*Value: (?<value>[^\r\n]+)\r?$", RegexOptions.Multiline).Cast<Match>().ToArray();
        Assert.Equal(count, matches.Length);
        return matches.ToDictionary(match => match.Groups["key"].Value, match => match.Groups["value"].Value);
    }

    private static void AssertStringArray(Target target, string command, int count)
    {
        DumpArrayResult array = target.DumpArray(CommandAddress(command, "dumparray"));
        Assert.Equal("System.String[]", array.Name);
        Assert.Equal(count, array.NumberOfElements);
        Assert.Equal(Enumerable.Range(0, count), array.Elements.Select(element => element.Index));
        Assert.All(array.Elements, element => Assert.NotEqual(0ul, element.Address));
    }

    private static DumpObjResult DumpStruct(Target target, string command)
    {
        Match match = Regex.Match(command, @"^dumpvc (?<mt>(?:0x)?[0-9a-fA-F`]+) (?<address>(?:0x)?[0-9a-fA-F`]+)$");
        Assert.True(match.Success, $"expected a dumpvc command, got: {command}");
        return target.DumpVc(ObjectCommandParsing.Hex(match.Groups["mt"].Value), ObjectCommandParsing.Hex(match.Groups["address"].Value));
    }

    private static void AssertStructFields(DumpObjResult value, int expectedInt)
    {
        Assert.Equal("DictionaryStructMarker", value.Name);
        ObjFieldRow integer = value.Field("IntValue");
        Assert.Equal("instance", integer.Attr);
        Assert.Equal("System.Int32", integer.Type);
        Assert.True(integer.IsValueType);
        Assert.Equal(expectedInt, int.Parse(integer.Value, CultureInfo.InvariantCulture));
        ObjFieldRow text = value.Field("StringValue");
        Assert.Equal("instance", text.Attr);
        Assert.Equal("System.String", text.Type);
        Assert.False(text.IsValueType);
        Assert.NotEqual(0ul, ObjectCommandParsing.Hex(text.Value));
        ObjFieldRow date = value.Field("Date");
        Assert.Equal("instance", date.Attr);
        Assert.Equal("System.DateTime", date.Type);
        Assert.True(date.IsValueType);
        Assert.NotEqual(0ul, ObjectCommandParsing.Hex(date.Value));
    }

    private static ulong CommandAddress(string command, string name)
    {
        Match match = Regex.Match(command, $@"^{name} (?<address>(?:0x)?[0-9a-fA-F`]+)$");
        Assert.True(match.Success, $"expected a {name} command, got: {command}");
        ulong address = ObjectCommandParsing.Hex(match.Groups["address"].Value);
        Assert.NotEqual(0ul, address);
        return address;
    }
}
