// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Runtime
{
    internal sealed class LinuxSnapshotTarget : CustomDataTarget
    {
        private readonly int _pid;
        private readonly string _filename;

        public LinuxSnapshotTarget(IDataReader reader, int pid, string filename) : base(reader)
        {
            _pid = pid;
            _filename = filename;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            try
            {
                File.Delete(_filename);
            }
            catch
            {
            }
        }

        public override string ToString() => $"{_filename} (snapshot of pid:{_pid:x})";

        public static LinuxSnapshotTarget CreateSnapshotFromProcess(int pid)
        {
            string? dumpPath = Path.GetTempFileName();
            try
            {
                try
                {
                    DiagnosticsClient client = new(pid);
                    client.WriteDump(DumpType.Full, dumpPath, logDumpGeneration: false);
                }
                catch (ServerErrorException sxe)
                {
                    throw new ArgumentException($"Unable to create a snapshot of process {pid:x}.", sxe);
                }

                LinuxSnapshotTarget result = new(new CoredumpReader(dumpPath, File.OpenRead(dumpPath), leaveOpen: false), pid, dumpPath);
                dumpPath = null;
                return result;
            }
            finally
            {
                if (dumpPath != null)
                    File.Delete(dumpPath);
            }
        }
    }
}