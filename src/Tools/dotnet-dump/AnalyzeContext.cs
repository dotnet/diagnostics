// --------------------------------------------------------------------
// 
// Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// --------------------------------------------------------------------
using Microsoft.Diagnostics.Runtime;
using SOS;
using System;
using System.CommandLine;
using System.Threading;

namespace Microsoft.Diagnostic.Tools.Dump
{
    /// <summary>
    /// The the common context for analyze commands
    /// </summary>
    public class AnalyzeContext: ISOSHostContext
    {
        private readonly IConsole _console;
        private ClrRuntime _runtime;
        private SOSHost _sosHost;

        public AnalyzeContext(IConsole console, DataTarget target, Action exit)
        {
            _console = console;
            Target = target;
            Exit = exit;
        }

        /// <summary>
        /// ClrMD data target
        /// </summary>
        public DataTarget Target { get; }

        /// <summary>
        /// ClrMD runtime info
        /// </summary>
        public ClrRuntime Runtime
        {
            get 
            {
                if (_runtime == null)
                {
                    if (Target.ClrVersions.Count != 1) {
                        throw new InvalidOperationException("More or less than 1 CLR version is present");
                    }
                    _runtime = Target.ClrVersions[0].CreateRuntime();
                }
                return _runtime;
            }
        }

        /// <summary>
        /// Returns the SOS host instance
        /// </summary>
        public SOSHost SOSHost
        {
            get 
            {
                if (_sosHost == null) {
                    _sosHost = new SOSHost(Target.DataReader, this);
                }
                return _sosHost;
            }
        }

        /// <summary>
        /// Delegate to invoke to exit repl
        /// </summary>
        public Action Exit { get; }

        /// <summary>
        /// Current OS thread Id
        /// </summary>
        public int CurrentThreadId { get; set; }

        /// <summary>
        /// Cancellation token for current command
        /// </summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Console write function
        /// </summary>
        /// <param name="text"></param>
        void ISOSHostContext.Write(string text)
        {
            _console.Out.Write(text);
        }
    }
}