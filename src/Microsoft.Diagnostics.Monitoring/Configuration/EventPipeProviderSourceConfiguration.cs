// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Monitoring
{
    public sealed class EventPipeProviderSourceConfiguration : MonitoringSourceConfiguration
    {
        private IList<EventPipeProvider> _providers;
        private bool _requestRundown;
        private int _bufferSizeInMB;

        public EventPipeProviderSourceConfiguration(bool requestRundown = true, int bufferSizeInMB = 256, params EventPipeProvider[] providers)
        {
            _providers = providers;
            _requestRundown = requestRundown;
            _bufferSizeInMB = bufferSizeInMB;
        }

        public override IList<EventPipeProvider> GetProviders()
        {
            return _providers;
        }

        public override bool RequestRundown => _requestRundown;

        public override int BufferSizeInMB => _bufferSizeInMB;
    }
}
