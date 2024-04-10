// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    /// <summary>
    /// Exception thrown to indicate that bits in the input cannot be parsed for whatever reason.
    /// </summary>
    public abstract class InputParsingException : Exception
    {
        public InputParsingException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Exception thrown to indicate unparsable bits found in the input data being parsed.
    /// </summary>
    public class BadInputFormatException : InputParsingException
    {
        public BadInputFormatException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Exception thrown to the virtual address/position is invalid
    /// </summary>
    public class InvalidVirtualAddressException : InputParsingException
    {
        public InvalidVirtualAddressException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Exception thrown to indicate errors during Layout construction. These errors are usually
    /// attributable to bugs in the parsing code, not errors in the input data.
    /// </summary>
    public class LayoutException : Exception
    {
        public LayoutException(string message)
            : base(message)
        {
        }
    }
}
