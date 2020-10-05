// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public interface ICounterPayload
    {
        string GetName();
        double GetValue();
        string GetCounterType();

        //Consider pushing this to extended counter interface
        string GetProvider();
        string GetDisplayName();
        string GetUnit();
        DateTime GetTimestamp();

        float GetInterval();
    }
}
