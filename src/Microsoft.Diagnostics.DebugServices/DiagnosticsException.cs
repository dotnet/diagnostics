// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
}
