// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    public static class ArrayHelper
    {
        /// <summary>
        /// Safe array allocator - turns OverFlows and OutOfMemory into BIF's.
        /// </summary>
        public static E[] New<E>(uint count)
        {
            E[] a;
            try
            {
                a = new E[count];
            }
            catch (Exception)
            {
                throw new BadInputFormatException("Internal overflow attempting to allocate an array of size " + count + ".");
            }
            return a;
        }
    }
}
