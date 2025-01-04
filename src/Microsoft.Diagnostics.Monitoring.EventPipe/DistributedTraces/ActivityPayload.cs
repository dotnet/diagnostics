// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal ref struct ActivityPayload
    {
        public ActivityData ActivityData;

        public ReadOnlySpan<KeyValuePair<string, object?>> Tags;
    }
}
