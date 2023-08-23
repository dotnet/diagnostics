// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using Microsoft.Diagnostics.Runtime.Implementation;

namespace Microsoft.Diagnostics.Runtime.DataReaders.Windows
{
    internal sealed class WindowsThreadSuspender : CriticalFinalizerObject, IDisposable
    {
        private readonly object _sync = new();
        private readonly int _pid;
        private volatile int[]? _suspendedThreads;

        public WindowsThreadSuspender(int pid)
        {
            _pid = pid;
            _suspendedThreads = SuspendThreads();
        }

        private int[] SuspendThreads()
        {
            bool permissionFailure = false;
            HashSet<int>? suspendedThreads = new();

            // A thread may create more threads while we are in the process of walking the list.  We will keep looping through
            // the thread list over and over until we find that we haven't found any new threads to suspend.
            try
            {
                int originalCount;
                do
                {
                    originalCount = suspendedThreads.Count;

                    Process process;
                    try
                    {
                        process = Process.GetProcessById(_pid);
                    }
                    catch (ArgumentException e)
                    {
                        throw new InvalidOperationException($"Unable to inspect process {_pid:x}.", e);
                    }

                    foreach (ProcessThread? thread in process.Threads)
                    {
                        if (thread != null)
                        {
                            if (suspendedThreads.Contains(thread.Id))
                                continue;

                            using SafeWin32Handle threadHandle = WindowsProcessDataReader.OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);
                            if (threadHandle.IsInvalid || WindowsProcessDataReader.SuspendThread(threadHandle.DangerousGetHandle()) == -1)
                            {
                                permissionFailure = true;
                                continue;
                            }

                            suspendedThreads.Add(thread.Id);
                        }
                    }
                } while (originalCount != suspendedThreads.Count);

                // If we fail to suspend any thread then we didn't have permission.  We'll throw an exception in that case.  If
                // we fail to suspend a few of the threads we'll treat that as non-fatal.
                if (permissionFailure && suspendedThreads.Count == 0)
                    throw new InvalidOperationException($"Unable to suspend threads of process {_pid:x}.");

                int[] result = suspendedThreads.ToArray();
                suspendedThreads = null;
                return result;
            }
            finally
            {
                if (suspendedThreads != null)
                    ResumeThreads(suspendedThreads);
            }
        }

        private void ResumeThreads(IEnumerable<int> suspendedThreads)
        {
            foreach (int threadId in suspendedThreads)
            {
                using SafeWin32Handle threadHandle = WindowsProcessDataReader.OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)threadId);
                if (threadHandle.IsInvalid || WindowsProcessDataReader.ResumeThread(threadHandle.DangerousGetHandle()) == -1)
                {
                    // If we fail to resume a thread we are in a bit of trouble because the target process is likely in a bad
                    // state.  This shouldn't ever happen, but if it does there's nothing we can do about it.  We'll log an event
                    // here but we won't throw an exception for a few reasons:
                    //     1.  We really never expect this to happen.  Why would we be able to suspend a thread but not resume it?
                    //     2.  We want to finish resuming threads.
                    //     3.  There's nothing the caller can really do about it.

                    Trace.WriteLine($"Failed to resume thread id:{threadId:id} in pid:{_pid:x}.");
                }
            }
        }

        ~WindowsThreadSuspender()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool _)
        {
            lock (_sync)
            {
                if (_suspendedThreads != null)
                {
                    int[] suspendedThreads = _suspendedThreads;
                    _suspendedThreads = null;
                    ResumeThreads(suspendedThreads);
                }
            }
        }
    }
}