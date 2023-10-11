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

    /// <summary>
    /// Thrown if a command is not found.
    /// </summary>
    public class CommandNotFoundException : DiagnosticsException
    {
        public const string NotFoundMessage = $"Unrecognized SOS command";

        public CommandNotFoundException(string message)
            : base(message)
        {
        }

        public CommandNotFoundException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown if a command is not found.
    /// </summary>
    public class CommandParsingException : DiagnosticsException
    {
        /// <summary>
        /// The detailed help of the command
        /// </summary>
        public string DetailedHelp { get; }

        public CommandParsingException(string message, string detailedHelp)
            : base(message)
        {
            DetailedHelp = detailedHelp;
        }

        public CommandParsingException(string message, string detailedHelp, Exception innerException)
            : base(message, innerException)
        {
            DetailedHelp = detailedHelp;
        }
    }
}
