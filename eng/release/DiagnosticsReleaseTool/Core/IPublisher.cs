// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReleaseTool.Core
{
    public interface IPublisher : IDisposable
    {
        Task<string> PublishFileAsync(FileMapping fileData, CancellationToken ct);
    }
}
