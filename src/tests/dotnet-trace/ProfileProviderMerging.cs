// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using Microsoft.Diagnostics.NETCore.Client;
using System;
using Xunit;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.Tracing;

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
            Dictionary<string, string> enabledBy = new Dictionary<string, string>();

            List<EventPipeProvider> parsedProviders = Extensions.ToProviders(providerToParse);

            foreach (var provider in parsedProviders)
            {
                enabledBy[provider.Name] = "--providers";
            }

            var selectedProfile = ListProfilesCommandHandler.DotNETRuntimeProfiles
                .FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(selectedProfile);

            Profile.MergeProfileAndProviders(selectedProfile, parsedProviders, enabledBy);

            var enabledProvider = parsedProviders.SingleOrDefault(p => p.Name == "Microsoft-Windows-DotNETRuntime");

            // Assert that our specified provider overrides the version in the profile
            Assert.True(enabledProvider.Keywords == (long)(-1));
            Assert.True(enabledProvider.EventLevel == EventLevel.Verbose);
            Assert.True(enabledBy[enabledProvider.Name] == "--providers");
        }
    }
}