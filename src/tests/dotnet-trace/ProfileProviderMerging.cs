// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using Microsoft.Diagnostics.NETCore.Client;
using Xunit;

namespace Microsoft.Diagnostics.Tools.Trace
{
    public class ProfileProviderMergeTests
    {
        [Theory]
        [InlineData("cpu-sampling", "Microsoft-Windows-DotNETRuntime")]
        [InlineData("gc-verbose", "Microsoft-Windows-DotNETRuntime")]
        [InlineData("gc-collect", "Microsoft-Windows-DotNETRuntime")]
        public void DuplicateProvider_CorrectlyOverrides(string profileName, string providerToParse)
        {
            Dictionary<string, string> enabledBy = new();

            List<EventPipeProvider> parsedProviders = Extensions.ToProviders(providerToParse);

            foreach (EventPipeProvider provider in parsedProviders)
            {
                enabledBy[provider.Name] = "--providers";
            }

            Profile selectedProfile = ListProfilesCommandHandler.DotNETRuntimeProfiles
                .FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(selectedProfile);

            Profile.MergeProfileAndProviders(selectedProfile, parsedProviders, enabledBy);

            EventPipeProvider enabledProvider = parsedProviders.SingleOrDefault(p => p.Name == "Microsoft-Windows-DotNETRuntime");

            // Assert that our specified provider overrides the version in the profile
            Assert.True(enabledProvider.Keywords == (long)(-1));
            Assert.True(enabledProvider.EventLevel == EventLevel.Verbose);
            Assert.True(enabledBy[enabledProvider.Name] == "--providers");
        }
    }
}
