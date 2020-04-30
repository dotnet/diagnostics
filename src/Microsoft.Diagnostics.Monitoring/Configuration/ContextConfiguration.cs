// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring
{
    /// <summary>
    /// Do not rename these fields. These are used to bind to the app's configuration.
    /// </summary>
    public class ContextConfiguration
    {
        public string Namespace { get; set; }

        public string Node { get; set; }
    }
}
