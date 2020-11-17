// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.Serialization;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Models
{
    [DataContract(Name = "Process")]
    public class ProcessIdentifierModel
    {
        [DataMember(Name = "pid")]
        public int Pid { get; set; }

        [DataMember(Name = "uid")]
        public Guid Uid { get; set; }

        internal static ProcessIdentifierModel FromProcessInfo(IProcessInfo processInfo)
        {
            return new ProcessIdentifierModel()
            {
                Pid = processInfo.EndpointInfo.ProcessId,
                Uid = processInfo.EndpointInfo.RuntimeInstanceCookie
            };
        }
    }
}
