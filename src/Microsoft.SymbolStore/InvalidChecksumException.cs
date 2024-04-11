// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
