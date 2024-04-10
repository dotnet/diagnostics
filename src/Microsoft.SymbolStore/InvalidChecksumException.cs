using System;

namespace Microsoft.SymbolStore
{
    public class InvalidChecksumException : Exception
    {
        public InvalidChecksumException(string message) : base(message)
        {

        } 
    }
}
