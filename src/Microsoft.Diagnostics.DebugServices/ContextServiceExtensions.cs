﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class ContextServiceExtensions
    {
        /// <summary>
        /// Returns the current target
        /// </summary>
        public static ITarget GetCurrentTarget(this IContextService contextService)
        {
            return contextService.Services.GetService<ITarget>();
        }

        /// <summary>
        /// Returns the current thread
        /// </summary>
        public static IThread GetCurrentThread(this IContextService contextService)
        {
            return contextService.Services.GetService<IThread>();
        }

        /// <summary>
        /// Returns the current runtime
        /// </summary>
        public static IRuntime GetCurrentRuntime(this IContextService contextService)
        {
            return contextService.Services.GetService<IRuntime>();
        }
    }
}
