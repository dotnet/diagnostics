﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System.Collections.Generic;
using System.Linq;

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

        public static void MergeProfileAndProviders(Profile selectedProfile, List<EventPipeProvider> providerCollection, Dictionary<string, string> enabledBy)
        {
            var profileProviders = new List<EventPipeProvider>();
            // If user defined a different key/level on the same provider via --providers option that was specified via --profile option,
            // --providers option takes precedence. Go through the list of providers specified and only add it if it wasn't specified
            // via --providers options.
            if (selectedProfile.Providers != null)
            {
                foreach (EventPipeProvider selectedProfileProvider in selectedProfile.Providers)
                {
                    bool shouldAdd = true;

                    foreach (EventPipeProvider providerCollectionProvider in providerCollection)
                    {
                        if (providerCollectionProvider.Name.Equals(selectedProfileProvider.Name))
                        {
                            shouldAdd = false;
                            break;
                        }
                    }

                    if (shouldAdd)
                    {
                        enabledBy[selectedProfileProvider.Name] = "--profile ";
                        profileProviders.Add(selectedProfileProvider);
                    }
                }
            }
            providerCollection.AddRange(profileProviders);
        }
    }
}
