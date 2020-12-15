// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Models
{
    [DataContract(Name = "EventPipeConfiguration")]
    public class EventPipeConfigurationModel
    {
        [DataMember(Name = "providers", IsRequired = true)]
        public EventPipeProviderModel[] Providers { get; set; }

        [DataMember(Name = "requestRundown")]
        public bool RequestRundown { get; set; } = true;

        [DataMember(Name = "bufferSizeInMB")]
        [Range(1, 1024)]
        public int BufferSizeInMB { get; set; } = 256;
    }
}
