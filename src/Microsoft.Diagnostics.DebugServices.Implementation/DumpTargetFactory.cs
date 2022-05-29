// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public sealed class DumpTargetFactory(IHost host) : IDumpTargetFactory
    {
        private readonly IHost _host = host;

        public ITarget OpenDump(string fileName)
        {
            DataTarget dataTarget;
            OSPlatform targetPlatform;
            try
            {
                fileName = Path.GetFullPath(fileName);
                dataTarget = DataTarget.LoadDump(fileName);
                targetPlatform = dataTarget.DataReader.TargetPlatform;
            }
            catch (Exception ex)
            {
                throw new DiagnosticsException(ex.Message, ex);
            }

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && (targetPlatform != OSPlatform.OSX))
                {
                    throw new NotSupportedException("Analyzing Windows or Linux dumps not supported when running on MacOS");
                }
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && (targetPlatform != OSPlatform.Linux))
                {
                    throw new NotSupportedException("Analyzing Windows or MacOS dumps not supported when running on Linux");
                }
                return new TargetFromDataReader(dataTarget, targetPlatform, _host, fileName);
            }
            catch (Exception)
            {
                dataTarget.Dispose();
                throw;
            }
        }
    }
}
