using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring
{
    internal sealed class AksContextConfiguration 
    {
        private const string NamespacePath = @"/var/run/secrets/kubernetes.io/serviceaccount/namespace";

        private string _namespace;
        private string _node;

        public AksContextConfiguration()
        {
        }

        public string Node
        {
            get
            {
                if (_node == null)
                {
                    _node = Environment.GetEnvironmentVariable("HOSTNAME");
                    if (string.IsNullOrEmpty(_node))
                    {
                        _node = Environment.MachineName;
                    }
                }
                return _node;
            }
        }

        public string Namespace
        {
            get
            {
                if (_namespace == null)
                {
                    try
                    {
                        _namespace = File.ReadAllText(NamespacePath);
                    }
                    catch
                    {
                        _namespace = "";
                    }
                }
                return _namespace;
            }
        }
    }
}
