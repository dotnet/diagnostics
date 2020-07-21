using System.Runtime.Serialization;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Models
{
    [DataContract(Name = "Process")]
    public class ProcessModel
    {
        [DataMember(Name = "pid")]
        public int Pid { get; set; }
    }
}