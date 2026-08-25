// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Field-level inspection: <c>!dumpobj</c>'s <c>Fields</c> table and <c>!dumpvc</c> (value class), driven by
/// the debuggee's <c>FieldMarker</c> object, whose fields hold known values mirrored from the target source
/// (<see cref="TestTargets.SosHarnessScenarios"/>). dumpobj is asserted to report each field's type, value
/// kind, and exact value; the embedded <c>ValueMarker</c> struct field then feeds <c>dumpvc</c> (its method
/// table + inline address come straight from dumpobj's row), whose own fields are checked the same way.
/// </summary>
public sealed class ObjectFieldsTests
{
    public static TheoryData<TestConfig> Matrix => TestMatrices.CoreFrameworkConditional([TargetCatalog.Scenarios]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpObj_Fields_ReportKnownValues(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        DumpObjResult obj = target.DumpObj(target.FindUniqueObject("FieldMarker"));
        Assert.Equal("FieldMarker", obj.Name);
        Assert.NotEmpty(obj.Fields);

        ObjFieldRow intField = obj.Field("IntField");
        Assert.Equal("System.Int32", intField.Type);
        Assert.True(intField.IsValueType);
        Assert.Equal("instance", intField.Attr);
        Assert.Equal(TestTargets.SosHarnessScenarios.FieldMarkerInt, int.Parse(intField.Value, CultureInfo.InvariantCulture));

        ObjFieldRow longField = obj.Field("LongField");
        Assert.Equal("System.Int64", longField.Type);
        Assert.Equal(TestTargets.SosHarnessScenarios.FieldMarkerLong, long.Parse(longField.Value, CultureInfo.InvariantCulture));

        ObjFieldRow textField = obj.Field("TextField");
        Assert.Equal("System.String", textField.Type);
        Assert.False(textField.IsValueType);                  // a reference field
        Assert.NotEqual(0ul, ObjectCommandParsing.Hex(textField.Value)); // its value is the string's address
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpVc_ReadsEmbeddedStructFields(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        DumpObjResult obj = target.DumpObj(target.FindUniqueObject("FieldMarker"));

        // The Value field is an embedded value type; dumpobj prints its method table and inline address.
        ObjFieldRow valueField = obj.Field("Value");
        Assert.True(valueField.IsValueType);
        Assert.Equal("ValueMarker", valueField.Type);
        ulong vcAddress = ObjectCommandParsing.Hex(valueField.Value);
        Assert.NotEqual(0ul, vcAddress);

        DumpObjResult vc = target.DumpVc(valueField.MethodTable, vcAddress);
        Assert.Equal("ValueMarker", vc.Name);
        Assert.Equal(TestTargets.SosHarnessScenarios.ValueMarkerFirst, int.Parse(vc.Field("First").Value, CultureInfo.InvariantCulture));
        Assert.Equal(TestTargets.SosHarnessScenarios.ValueMarkerSecond, long.Parse(vc.Field("Second").Value, CultureInfo.InvariantCulture));
    }
}
