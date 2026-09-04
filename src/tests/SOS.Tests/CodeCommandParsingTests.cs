// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class CodeCommandParsingTests
{
    [Fact]
    public void ClrUParsesX86InstructionWithoutOffset()
    {
        ClrUResult result = new(new SosOutput("cdb", "clru", """
            Normal JIT generated code
            Type.Method()
            Begin 088837b0, size 10
            088837b0 03c1 add eax,ecx
            """));

        DisasmLine instruction = Assert.Single(result.Instructions);
        Assert.Null(instruction.Offset);
        Assert.Equal(0x088837b0ul, instruction.Address);
        Assert.Equal("03c1", instruction.Bytes);
        Assert.Equal("add", instruction.Mnemonic);
        Assert.Equal("eax,ecx", instruction.Operands);
        Assert.False(result.HasOffsets);
    }

    [Fact]
    public void ClrUParsesX86InstructionWithOffset()
    {
        ClrUResult result = new(new SosOutput("cdb", "clru -o", """
            Normal JIT generated code
            Type.Method()
            Begin 088837b0, size 10
            0000 088837b0 03c1 add eax,ecx
            """), hasOffsets: true);

        DisasmLine instruction = Assert.Single(result.Instructions);
        Assert.Equal(0, instruction.Offset);
        Assert.Equal(0x088837b0ul, instruction.Address);
        Assert.Equal("03c1", instruction.Bytes);
        Assert.Equal("add", instruction.Mnemonic);
        Assert.True(result.HasOffsets);
    }

    [Fact]
    public void GcInfoParsesX86LegacyEncoding()
    {
        GcInfoResult result = new(new SosOutput("cdb", "gcinfo", """
            entry point 08755230
            GC info 0A7596E4
            Method info block:
                method      size   = 0022
            Pointer table:
            F1 4F    FF ...| 0017        reg ECX becoming live
            0E    00 39 ...| 001D        reg ECX becoming dead
            """));

        Assert.Equal(0x08755230ul, result.EntryPoint);
        Assert.Equal(0x0A7596E4ul, result.GcInfoAddress);
        Assert.Equal(0x22, result.CodeSize);
        Assert.Equal(2, result.Transitions.Count);
        Assert.Contains("becoming live", result.Transitions[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("becoming dead", result.Transitions[1], StringComparison.OrdinalIgnoreCase);
    }
}
