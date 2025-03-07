// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal readonly record struct LogRecordException
    {
        public LogRecordException(
            string? exceptionType,
            string? message,
            string? stackTrace)
        {
            ExceptionType = exceptionType;
            Message = message;
            StackTrace = stackTrace;
        }

        public readonly string? ExceptionType;

        public readonly string? Message;

        public readonly string? StackTrace;
    }
}
