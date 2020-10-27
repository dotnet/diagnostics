using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring
{
    internal class PipelineException : Exception
    {
        public PipelineException(string message) : base(message) { }
        public PipelineException(string message, Exception inner) : base(message, inner) { }
    }
}
