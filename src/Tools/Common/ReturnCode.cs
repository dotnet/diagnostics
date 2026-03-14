// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Internal.Common.Utils
{
    internal enum ReturnCode
    {
        Ok,
        SessionCreationError,
        TracingError,
        ArgumentError,
        PlatformNotSupportedError,
        UnknownError
    }
}
