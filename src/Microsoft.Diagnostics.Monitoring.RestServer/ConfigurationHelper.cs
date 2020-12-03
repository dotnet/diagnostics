// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    internal static class ConfigurationHelper
    {
        public static string MakeKey(string parent, string child)
        {
            return FormattableString.Invariant($"{parent}:{child}");
        }

        public static string[] SplitValue(string value)
        {
            return value.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
