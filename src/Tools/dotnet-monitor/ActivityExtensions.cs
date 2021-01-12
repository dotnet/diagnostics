// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal static class ActivityExtensions
    {
        public static string GetSpanId(this Activity activity)
        {
            switch (activity.IdFormat)
            {
                case ActivityIdFormat.Hierarchical:
                    return activity.Id;
                case ActivityIdFormat.W3C:
                    return activity.SpanId.ToHexString();
            }
            return string.Empty;
        }

        public static string GetParentId(this Activity activity)
        {
            switch (activity.IdFormat)
            {
                case ActivityIdFormat.Hierarchical:
                    return activity.ParentId;
                case ActivityIdFormat.W3C:
                    return activity.ParentSpanId.ToHexString();
            }
            return string.Empty;
        }

        public static string GetTraceId(this Activity activity)
        {
            switch (activity.IdFormat)
            {
                case ActivityIdFormat.Hierarchical:
                    return activity.RootId;
                case ActivityIdFormat.W3C:
                    return activity.TraceId.ToHexString();
            }
            return string.Empty;
        }
    }
}
