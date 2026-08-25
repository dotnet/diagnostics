// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// A single combined smoke for the rarely-used signature commands <c>!dumpsig</c> and <c>!dumpsigelem</c>.
/// The debuggee's <c>FieldMarker</c> carries two raw <c>COR_SIGNATURE</c> blobs in <c>byte[]</c> fields — a
/// method signature (<c>[DEFAULT] Void ()</c>) and a single <c>ELEMENT_TYPE_I4</c> element — so the test
/// can take the address of the first byte (via <c>dumparray</c>) and decode a genuine signature, then also
/// checks the documented usage/argument contract.
/// </summary>
public sealed class DumpSigTests
{
    public static TheoryData<TestConfig> Matrix => TestMatrices.CoreFrameworkConditional([TargetCatalog.Scenarios]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpSig_And_DumpSigElem(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        DumpObjResult holder = target.DumpObj(target.FindUniqueObject("FieldMarker"));
        ulong methodSig = FirstByteAddress(target, holder.Field("MethodSignature").Value);
        ulong sigElement = FirstByteAddress(target, holder.Field("SignatureElement").Value);

        // dumpsig decodes the whole method signature: [DEFAULT] Void ().
        SosOutput sig = target.Sos($"dumpsig {methodSig:x} 0");
        Assert.DoesNotContain("Invalid signature", sig.Text);
        sig.AssertContains("Void");

        // dumpsigelem decodes a single element (ELEMENT_TYPE_I4) without error.
        SosOutput elem = target.Sos($"dumpsigelem {sigElement:x} 0");
        Assert.DoesNotContain("Invalid signature", elem.Text);
        Assert.NotEmpty(elem.Text.Trim());

        // Documented argument contract: no sigaddr prints the usage line.
        target.Sos("dumpsig").AssertContains("dumpsig <sigaddr>");
        target.Sos("dumpsigelem").AssertContains("dumpsigelem <sigaddr>");
    }

    private static ulong FirstByteAddress(Target target, string arrayFieldValue)
    {
        ulong array = ObjectCommandParsing.Hex(arrayFieldValue);
        ArrayElement first = target.DumpArray(array).Elements[0];
        return first.Address;
    }
}
