// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class ThreadExtensions
    {
        public static unsafe void GetThreadContext(this IThread thread, IntPtr context, int contextSize)
        {
            GetThreadContext(thread, new(context.ToPointer(), contextSize));
        }

        public static void GetThreadContext(this IThread thread, Span<byte> context)
        {
            ReadOnlySpan<byte> registerContext = thread.GetThreadContext();
            context.Clear();
            registerContext.Slice(0, Math.Min(registerContext.Length, context.Length)).CopyTo(context);
        }
    }
}
