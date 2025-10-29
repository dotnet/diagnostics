// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Tools.Trace
{
    [Obsolete("ErrorCodes is deprecated. Use ReturnCode from Microsoft.Internal.Common.Utils instead.")]
    internal static class ErrorCodes
    {
        public const int SessionCreationError = 1;
        public const int TracingError = 2;
        public const int ArgumentError = 3;
        public const int UnknownError = 4;
    }
}
