// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Tools.Counters
{
    // This is an exception whose error message is intended to be displayed
    // to the user an error on the command line
    //
    // These should be error conditions that we specifically anticipated might
    // occur and we have created error text that will help the user understand and
    // ideally resolve the issue. No stack trace or any other telemetry
    // is going to be given because the presumption is we already understand the
    // issue and the error text adequately explains it to the user.
    //
    // For any other error conditions that were unanticipated or do not have
    // contextualized error messages, don't use this type.
    internal class CommandLineErrorException : Exception
    {
        public CommandLineErrorException(string errorMessage) : base(errorMessage) { }
    }
}
