// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Runtime.Serialization;
using Microsoft.Diagnostics.Monitoring.RestServer.Validation;
using Newtonsoft.Json;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Models
{
    [DataContract(Name = "EventPipeProvider")]
    public class EventPipeProviderModel
    {
        [DataMember(Name = "name", IsRequired = true)]
        public string Name { get; set; }

        [DataMember(Name = "keywords", IsRequired = true)]
        [IntegerOrHexString]
        public string Keywords { get; set; }

        [DataMember(Name = "eventLevel", IsRequired = true)]
        public EventPipeProviderEventLevel EventLevel { get; set; }

        [DataMember(Name = "arguments")]
        public IDictionary<string, string> Arguments { get; set; }
    }
}
