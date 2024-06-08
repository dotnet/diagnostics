// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
// This class describes the various strategies for retrying a command.
// The rough idea is that these numbers form a state machine.
// Any time a command execution fails, a retry will be attempted by matching the
// condition of the config as well as this strategy number to generate a
// modified config as well as a modified strategy.
//
// This is designed with forward compatibility in mind. We might have newer
// capabilities that only exists in newer runtimes, but we will never know exactly
// how we should retry. So this give us a way to encode the retry strategy in the
// profiles without having to introducing new concepts.
//
namespace Microsoft.Diagnostics.Tools.Trace
{
    internal enum RetryStrategy
    {
        NothingToRetry = 0,
        DropKeywordKeepRundown = 1,
        DropKeywordDropRundown = 2,
        ForbiddenToRetry = 3
    }
}
