// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.Tools.RuntimeClient
{
    public enum DiagnosticMessageType : uint
    {
        StartSession = 1024,
        StopSession,
        Stream,
    }
}
