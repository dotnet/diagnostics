// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Xunit;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// Tests that <see cref="DiagnosticsClient.ValidateResponseMessage"/> maps the runtime's wire error
    /// codes to distinct exception types, so callers can tell "the runtime doesn't recognize this command"
    /// (UnknownCommand) apart from "the command was understood but its arguments were rejected"
    /// (InvalidArgument).
    /// </summary>
    public class ValidateResponseMessageTests
    {
        private static IpcMessage ErrorResponse(DiagnosticsIpcError error) =>
            new(DiagnosticsServerCommandSet.Server, (byte)DiagnosticsServerResponseId.Error, BitConverter.GetBytes((uint)error));

        [Fact]
        public void UnknownCommand_ThrowsUnknownCommandException()
        {
            UnknownCommandException ex = Assert.Throws<UnknownCommandException>(() =>
                DiagnosticsClient.ValidateResponseMessage(ErrorResponse(DiagnosticsIpcError.UnknownCommand), "Start"));

            // Derives from UnsupportedCommandException so existing catch blocks keep working.
            Assert.IsAssignableFrom<UnsupportedCommandException>(ex);
        }

        [Fact]
        public void InvalidArgument_ThrowsInvalidCommandArgumentException_NotUnknownCommand()
        {
            InvalidCommandArgumentException ex = Assert.Throws<InvalidCommandArgumentException>(() =>
                DiagnosticsClient.ValidateResponseMessage(ErrorResponse(DiagnosticsIpcError.InvalidArgument), "Start"));

            // An invalid-argument rejection must not be mistaken for an unknown command.
            Assert.IsNotType<UnknownCommandException>(ex);
            Assert.IsAssignableFrom<UnsupportedCommandException>(ex);
        }

        [Fact]
        public void OkResponse_DoesNotThrow()
        {
            IpcMessage ok = new(DiagnosticsServerCommandSet.Server, (byte)DiagnosticsServerResponseId.OK);
            Assert.True(DiagnosticsClient.ValidateResponseMessage(ok, "Start"));
        }
    }
}
