// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS.Hosting
{
    internal sealed class RuntimeLibraryProvider : COMCallableIUnknown, IDisposable
    {
        private static readonly Guid IID_ICLRDebuggingLibraryProvider2 = new("E04E2FF1-DCFD-45D5-BCD1-16FFF2FAF7BA");

        private readonly Func<string> _getDbiPath;
        private readonly Func<string> _getDacPath;
        private readonly bool _verifySignature;
        private readonly List<IDisposable> _verifiedFiles = [];

        public IntPtr ILibraryProvider { get; }

        public RuntimeLibraryProvider(
            Func<string> getDbiPath,
            Func<string> getDacPath,
            bool verifySignature)
        {
            _getDbiPath = getDbiPath ?? throw new ArgumentNullException(nameof(getDbiPath));
            _getDacPath = getDacPath ?? throw new ArgumentNullException(nameof(getDacPath));
            _verifySignature = verifySignature;

            VTableBuilder builder = AddInterface(IID_ICLRDebuggingLibraryProvider2, validate: false);
            builder.AddMethod(new ProvideLibrary2Delegate(ProvideLibrary2));
            ILibraryProvider = builder.Complete();

            AddRef();
        }

        void IDisposable.Dispose()
        {
            foreach (IDisposable verifiedFile in _verifiedFiles)
            {
                verifiedFile.Dispose();
            }
            _verifiedFiles.Clear();
            this.ReleaseWithCheck();
        }

        private int ProvideLibrary2(
            IntPtr self,
            string fileName,
            uint timeStamp,
            uint sizeOfImage,
            out IntPtr modulePath)
        {
            modulePath = IntPtr.Zero;

            try
            {
                string path = fileName?.IndexOf("mscordbi", StringComparison.OrdinalIgnoreCase) >= 0
                    ? _getDbiPath()
                    : _getDacPath();
                if (string.IsNullOrEmpty(path))
                {
                    Trace.TraceError($"RuntimeLibraryProvider: could not resolve {fileName}");
                    return HResult.E_NOINTERFACE;
                }

                if (_verifySignature && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Trace.TraceInformation($"RuntimeLibraryProvider: verifying Authenticode signature {path}");
                    if (!AuthenticodeUtil.VerifyDacDll(path, out IDisposable fileLock))
                    {
                        fileLock?.Dispose();
                        Trace.TraceError($"RuntimeLibraryProvider: Authenticode verification failed for {path}");
                        return HResult.E_NOINTERFACE;
                    }
                    _verifiedFiles.Add(fileLock);
                }

                modulePath = Marshal.StringToCoTaskMemUni(path);
                Trace.TraceInformation($"RuntimeLibraryProvider: resolved {fileName} to {path}");
                return HResult.S_OK;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"RuntimeLibraryProvider: resolving {fileName} failed: {ex}");
                return HResult.E_NOINTERFACE;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ProvideLibrary2Delegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPWStr)] string fileName,
            [In] uint timeStamp,
            [In] uint sizeOfImage,
            [Out] out IntPtr modulePath);
    }
}
