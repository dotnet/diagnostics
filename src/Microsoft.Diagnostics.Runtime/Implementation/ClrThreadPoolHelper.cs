// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class ClrThreadPoolHelper : IClrThreadPoolHelper
    {
        private readonly SOSDac _sos;

        public ClrThreadPoolHelper(SOSDac sos)
        {
            _sos = sos;
        }

        public bool GetLegacyThreadPoolData(out ThreadPoolData data, out bool usePortableThreadPool)
        {
            HResult hr = _sos.GetThreadPoolData(out data);
            usePortableThreadPool = hr == HResult.E_NOTIMPL;
            return hr;
        }

        public bool GetLegacyWorkRequestData(ulong workRequest, out WorkRequestData workRequestData)
        {
            return _sos.GetWorkRequestData(workRequest, out workRequestData);
        }
    }
}