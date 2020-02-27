using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring
{
    public class ContextConfiguration
    {
        private const string DefaultNamespace = "default";

        public virtual string Namespace => DefaultNamespace;

        public virtual string Node => Environment.MachineName;

        public ContextConfiguration()
        {
        }
    }
}
