// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal sealed class Profile
    {
        public Profile(string name, IEnumerable<Provider> providers, string description)
        {
            Name = name;
            Providers = providers == null ? Enumerable.Empty<Provider>() : new List<Provider>(providers).AsReadOnly();
            Description = description;
        }

        public string Name { get; }

        public IEnumerable<Provider> Providers { get; }

        public string Description { get; }
    }
}
