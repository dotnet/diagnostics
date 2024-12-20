// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Collections.Generic;
using System;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal readonly ref struct LogRecordScopeContainer
    {
        private readonly ReadOnlySpan<LogObject> _Scopes;

        public LogRecordScopeContainer(
            ReadOnlySpan<LogObject> scopes)
        {
            _Scopes = scopes;
        }

        public void ForEachScope<T>(LogRecordScopeAction<T> callback, ref T state)
        {
            foreach (LogObject scope in _Scopes)
            {
                callback(scope.ToSpan(), ref state);
            }
        }

        public delegate void LogRecordScopeAction<T>(
            ReadOnlySpan<KeyValuePair<string, object?>> attributes,
            ref T state);
    }
}
