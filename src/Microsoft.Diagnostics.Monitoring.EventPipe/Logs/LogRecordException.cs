// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Diagnostics;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal readonly struct LogRecordException : IEquatable<LogRecordException>
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

        public bool Equals(LogRecordException other)
        {
            return ExceptionType == other.ExceptionType
                && Message == other.Message
                && StackTrace == other.StackTrace;
        }

        public override bool Equals(object obj)
            => obj is LogRecordException ex && Equals(ex);

        public override int GetHashCode()
        {
            HashCode hash = default;

            hash.Add(ExceptionType);
            hash.Add(Message);
            hash.Add(StackTrace);

            return hash.ToHashCode();
        }

        public static bool operator ==(LogRecordException left, LogRecordException right)
            => left.Equals(right);

        public static bool operator !=(LogRecordException left, LogRecordException right)
            => !left.Equals(right);
    }
}
