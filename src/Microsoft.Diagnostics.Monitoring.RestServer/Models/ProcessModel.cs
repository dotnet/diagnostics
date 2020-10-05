using Microsoft.Diagnostics.Monitoring.Contracts;
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

        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "commandLine")]
        public string CommandLine { get; private set; }

        [DataMember(Name = "os")]
        public string OperatingSystem { get; private set; }

        [DataMember(Name = "architecture")]
        public string ProcessArchitecture { get; private set; }

        public static ProcessModel FromProcessInfo(IProcessInfo processInfo)
        {
            return new ProcessModel()
            {
                CommandLine = processInfo.CommandLine,
                Name = processInfo.ProcessName,
                OperatingSystem = processInfo.OperatingSystem,
                ProcessArchitecture = processInfo.ProcessArchitecture,
                Pid = processInfo.ProcessId,
                Uid = processInfo.RuntimeInstanceCookie
            };
        }
    }
}