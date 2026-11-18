// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.ExtensionCommands;
using Microsoft.FileFormats;
using Xunit;

namespace Microsoft.Diagnostics.ExtensionCommands.UnitTests
{
    public class ReaderLayoutTests
    {
        [Fact]
        public void CommandLayoutsRegisterExplicitly()
        {
            LayoutManager layouts = new LayoutManager()
                .AddPrimitives()
                .AddEnumTypes()
                .AddSizeT(8)
                .AddNullTerminatedString();

            ClrEngineMetrics.RegisterLayout(layouts);
            SpecialDiagInfoHeader.RegisterLayout(layouts);

            Assert.NotNull(layouts.GetLayout<ClrEngineMetrics>());
            Assert.NotNull(layouts.GetLayout<SpecialDiagInfoHeader>());
        }
    }
}
