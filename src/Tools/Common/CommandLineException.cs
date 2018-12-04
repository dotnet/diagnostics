using System;
using System.Runtime.Serialization;

namespace Microsoft.Internal.Utilities
{
    public class CommandLineException : Exception
    {
        public CommandLineException()
        {
        }

        public CommandLineException(string message) : base(message)
        {
        }

        public CommandLineException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected CommandLineException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
