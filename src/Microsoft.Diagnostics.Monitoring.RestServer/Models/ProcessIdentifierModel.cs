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

        public static ProcessIdentifierModel FromProcessInfo(IProcessInfo processInfo)
        {
            return new ProcessIdentifierModel()
            {
                Pid = processInfo.ProcessId,
                Uid = processInfo.RuntimeInstanceCookie
            };
        }
    }
}
