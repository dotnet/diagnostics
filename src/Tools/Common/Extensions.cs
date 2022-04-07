// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Microsoft.Tools.Common
{
    public static class Extenions
    {
        /// <summary>
        /// Allows the command handler to be included in the collection initializer.
        /// </summary>
        public static void Add(this Command command, ICommandHandler handler)
        {
            command.Handler = handler;
        }

        /// <summary>
        /// Get a value from the dictionary. If the key is not in the dictionary, initialize an entry using the provided Func
        /// </summary>
        public static TVal GetWithDefaultInitializer<TKey,TVal>(this Dictionary<TKey,TVal> dict, TKey key, Func<TKey, TVal> initializer)
        {
            if (!dict.ContainsKey(key))
                dict[key] = initializer(key);
            return dict[key];
        }
    }
}