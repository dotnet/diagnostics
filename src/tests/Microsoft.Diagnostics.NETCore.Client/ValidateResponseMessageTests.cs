// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Xunit;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// Tests that <see cref="DiagnosticsClient.ValidateResponseMessage"/> maps the runtime's wire error
    /// codes to the right exception types: UnknownCommand to UnsupportedCommandException (so gcdump/dotnet-trace
    /// can fall back or downgrade for a runtime too old for a command), and BadEncoding to BadEncodingException
    /// (a ServerErrorException that is deliberately not an UnsupportedCommandException, so a malformed payload is
    /// not mistaken for an unsupported command or swept into command-downgrade retries).
    /// </summary>
    public class ValidateResponseMessageTests
    {
        private static IpcMessage ErrorResponse(DiagnosticsIpcError error) =>
            new(DiagnosticsServerCommandSet.Server, (byte)DiagnosticsServerResponseId.Error, BitConverter.GetBytes((uint)error));

        [Fact]
        public void UnknownCommand_ThrowsUnsupportedCommandException()
        {
            // A command the runtime doesn't recognize (too old) surfaces as UnsupportedCommandException, which
            // gcdump/dotnet-trace catch to fall back or downgrade to an older command form.
            Assert.Throws<UnsupportedCommandException>(() =>
                DiagnosticsClient.ValidateResponseMessage(ErrorResponse(DiagnosticsIpcError.UnknownCommand), "Start"));
        }

        [Fact]
        public void BadEncoding_ThrowsBadEncodingException_NotUnsupportedCommand()
        {
            // A malformed payload for a recognized command returns BadEncoding. It must surface as a
            // BadEncodingException that is NOT an UnsupportedCommandException, so it is not swept into the
            // command-downgrade retries (EventPipeStreamProvider/dotnet-trace) and gcdump's
            // UnsupportedCommandException fallback does not catch it, letting a genuine payload/protocol bug surface.
            BadEncodingException ex = Assert.Throws<BadEncodingException>(() =>
                DiagnosticsClient.ValidateResponseMessage(ErrorResponse(DiagnosticsIpcError.BadEncoding), "Start"));

            Assert.IsNotAssignableFrom<UnsupportedCommandException>(ex);
            Assert.IsAssignableFrom<ServerErrorException>(ex);
        }

        [Fact]
        public void OkResponse_DoesNotThrow()
        {
            IpcMessage ok = new(DiagnosticsServerCommandSet.Server, (byte)DiagnosticsServerResponseId.OK);
            Assert.True(DiagnosticsClient.ValidateResponseMessage(ok, "Start"));
        }
    }
}
