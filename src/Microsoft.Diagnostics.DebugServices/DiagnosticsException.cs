// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Diagnostics exception
    /// </summary>
    public class DiagnosticsException : Exception
    {
        public DiagnosticsException()
            : base()
        {
        }

        public DiagnosticsException(string message)
            : base(message)
        {
        }

        public DiagnosticsException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown if a command is not supported on the configuration, platform or runtime
    /// </summary>
    public class CommandNotSupportedException : DiagnosticsException
    {
        public CommandNotSupportedException()
            : base()
        {
        }

        public CommandNotSupportedException(string message)
            : base(message)
        {
        }

        public CommandNotSupportedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
