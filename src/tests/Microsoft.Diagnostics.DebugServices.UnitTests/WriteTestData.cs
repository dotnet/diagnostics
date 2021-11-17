// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    [Command(Name = "writetestdata", Help = "Writes the test data xml file.")]
    public class WriteTestDataCommand : CommandBase
    {
        public ITarget Target { get; set; }

        [Argument(Name = "FileName", Help = "Test data file path.")]
        public string FileName { get; set; }

        public override void Invoke()
        {
            if (string.IsNullOrEmpty(FileName))
            {
                throw new DiagnosticsException("Test data file parameter needed");
            }
            var testDataWriter = new TestDataWriter();
            testDataWriter.Build(Target);
            testDataWriter.Write(FileName);
            WriteLine($"Test data written to {FileName} successfully");
        }
    }
}
