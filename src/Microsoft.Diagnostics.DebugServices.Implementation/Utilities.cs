// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public class Utilities
    {
        /// <summary>
        /// Combines two hash codes into a single hash code, in an order-dependent manner.
        /// </summary>
        /// <remarks>
        /// This function is neither commutative nor associative; the hash codes must be combined in
        /// a deterministic order.  Do not use this when hashing collections whose contents are
        /// nondeterministically ordered!
        /// </remarks>
        public static int CombineHashCodes(int hashCode0, int hashCode1)
        {
            unchecked {
                // This specific hash function is based on the Boost C++ library's CombineHash function:
                // http://stackoverflow.com/questions/4948780/magic-numbers-in-boosthash-combine
                // http://www.boost.org/doc/libs/1_46_1/doc/html/hash/combine.html 
                return hashCode0 ^ (hashCode1 + (int) 0x9e3779b9 + (hashCode0 << 6) + (hashCode0 >> 2));
            }
        }
    }
}
