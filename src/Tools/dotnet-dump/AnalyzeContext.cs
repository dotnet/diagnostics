// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using SOS;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.Diagnostics.Tools.Dump
{
    /// <summary>
    /// The the common context for analyze commands
    /// </summary>
    public class AnalyzeContext: ISOSHostContext
    {
        private readonly IConsole _console;
        private ClrRuntime _runtime;

        private static SOSHost s_sosHost;
        private static string s_tempDirectory;
        private static string s_dacFilePath;

        public AnalyzeContext(IConsole console, DataTarget target)
        {
            _console = console;
            Target = target;
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
                    ClrInfo clrInfo = Target.ClrVersions[0];
                    string dacFilePath = GetDacFile(clrInfo);
                    try
                    {
                        _runtime = clrInfo.CreateRuntime(dacFilePath);
                    }
                    catch (DllNotFoundException ex)
                    {
                        // This is a workaround for the Microsoft SDK docker images. Can fail when clrmd uses libdl.so 
                        // to create a symlink to and load the DAC module.
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        {
                            throw new DllNotFoundException("Problem initializing CLRMD. Try installing libc6-dev (apt-get install libc6-dev) to work around this problem.", ex);
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
                return _runtime;
            }
        }

        private string GetDacFile(ClrInfo clrInfo)
        {
            if (s_dacFilePath == null)
            {
                string dac = clrInfo.LocalMatchingDac;
                if (dac != null && File.Exists(dac))
                {
                    s_dacFilePath = dac;
                }
                else if (SymbolReader.IsSymbolStoreEnabled())
                {
                    string dacFileName = Path.GetFileName(dac ?? clrInfo.DacInfo.FileName);
                    if (dacFileName != null)
                    {
                        SymbolStoreKey key = null;

                        if (clrInfo.ModuleInfo.BuildId != null)
                        {
                            IEnumerable<SymbolStoreKey> keys = ELFFileKeyGenerator.GetKeys(
                                KeyTypeFlags.ClrKeys, clrInfo.ModuleInfo.FileName, clrInfo.ModuleInfo.BuildId, symbolFile: false, symbolFileName: null);

                            key = keys.SingleOrDefault((k) => Path.GetFileName(k.FullPathName) == dacFileName);
                        }
                        else
                        {
                            // Use the coreclr.dll's id (timestamp/filesize) to download the the dac module.
                            key = PEFileKeyGenerator.GetKey(dacFileName, clrInfo.ModuleInfo.TimeStamp, clrInfo.ModuleInfo.FileSize);
                        }

                        if (key != null)
                        {
                            if (s_tempDirectory == null)
                            {
                                int processId = Process.GetCurrentProcess().Id;
                                s_tempDirectory = Path.Combine(Path.GetTempPath(), "analyze" + processId.ToString());
                            }
                            // Now download the DAC module from the symbol server
                            s_dacFilePath = SymbolReader.GetSymbolFile(key, s_tempDirectory);
                        }
                    }
                }

                if (s_dacFilePath == null)
                {
                    throw new FileNotFoundException("Could not find matching DAC for this runtime: {0}", clrInfo.ModuleInfo.FileName);
                }
            }

            return s_dacFilePath;
        }

        /// <summary>
        /// Returns the SOS host instance
        /// </summary>
        public SOSHost SOSHost
        {
            get 
            {
                if (s_sosHost == null) {
                    s_sosHost = new SOSHost(Target.DataReader, this);
                    s_sosHost.InitializeSOSHost(s_tempDirectory, s_dacFilePath, dbiFilePath: null);
                }
                return s_sosHost;
            }
        }

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