// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal sealed class Profile
    {
        public Profile(string name, IEnumerable<EventPipeProvider> providers, string description)
        {
            Name = name;
            Providers = providers == null ? Enumerable.Empty<EventPipeProvider>() : new List<EventPipeProvider>(providers).AsReadOnly();
            Description = description;
        }

        public string Name { get; }

        public IEnumerable<EventPipeProvider> Providers { get; }

        public string Description { get; }

        public long RundownKeyword { get; set; } = EventPipeSession.DefaultRundownKeyword;

        public RetryStrategy RetryStrategy { get; set; } = RetryStrategy.NothingToRetry;

        public string VerbExclusivity { get; set; } = string.Empty;

        public string CollectLinuxArgs { get; set; } = string.Empty;
    }
}
