// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal sealed class StringBuilderPool
    {
        private StringBuilder _stringBuilder;
        private readonly int _initialCapacity;

        public StringBuilderPool(int initialCapacity = 64)
        {
            _initialCapacity = initialCapacity > 0 ? initialCapacity : 0;
        }

        // This code all assumes SOS runs single threaded.  We would want to change this
        // code to use Interlocked.Exchange if that ever changes.
        public StringBuilder Rent()
        {
            StringBuilder sb = _stringBuilder;
            _stringBuilder = null;

            if (sb is null)
            {
                sb = new StringBuilder(_initialCapacity);
            }
            else
            {
                sb.Clear();
            }

            return sb;
        }

        public void Return(StringBuilder sb)
        {
            if (sb.Capacity < 1024)
            {
                _stringBuilder = sb;
            }
        }
    }
}
