// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReleaseTool.Core
{
    public interface ILayoutWorker : IDisposable
    {
        ValueTask<LayoutWorkerResult> HandleFileAsync(FileInfo file, CancellationToken ct);
    }
}