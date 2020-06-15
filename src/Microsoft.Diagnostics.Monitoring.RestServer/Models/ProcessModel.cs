using System;
using System.Runtime.Serialization;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Models
{
    [DataContract(Name = "Process")]
    public class ProcessModel
    {
        [DataMember(Name = "pid")]
        public int Pid { get; set; }

        [DataMember(Name = "uid")]
        public Guid Uid { get; set; }

        public static ProcessModel FromProcessInfo(IProcessInfo processInfo)
        {
            return new ProcessModel() { Pid = processInfo.Pid, Uid = processInfo.Uid };
        }
    }
}