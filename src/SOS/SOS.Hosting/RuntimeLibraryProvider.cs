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
        private readonly ICollection<VerifiedLibraryCopy> _libraryCopies;
        private readonly bool _ownsLibraryCopies;

        public IntPtr ILibraryProvider { get; }

        /// <param name="libraryCopies">
        /// Receives the verified private copies handed out so the owner can delete them once the libraries are
        /// unloaded. When null, the provider deletes them on dispose.
        /// </param>
        public RuntimeLibraryProvider(
            Func<string> getDbiPath,
            Func<string> getDacPath,
            bool verifySignature,
            ICollection<VerifiedLibraryCopy> libraryCopies = null)
        {
            _getDbiPath = getDbiPath ?? throw new ArgumentNullException(nameof(getDbiPath));
            _getDacPath = getDacPath ?? throw new ArgumentNullException(nameof(getDacPath));
            _verifySignature = verifySignature;
            _libraryCopies = libraryCopies ?? new List<VerifiedLibraryCopy>();
            _ownsLibraryCopies = libraryCopies is null;

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
            if (_ownsLibraryCopies)
            {
                foreach (VerifiedLibraryCopy libraryCopy in _libraryCopies)
                {
                    libraryCopy.Dispose();
                }
                _libraryCopies.Clear();
            }
            this.ReleaseWithCheck();
        }

        internal int ProvideLibrary2(
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
                    // Verify and hand out a private copy so the original can't be swapped before it is loaded.
                    VerifiedLibraryCopy libraryCopy = VerifiedLibraryCopy.Create(path);
                    Trace.TraceInformation($"RuntimeLibraryProvider: verifying Authenticode signature {path} (copied to {libraryCopy.FilePath})");
                    if (!AuthenticodeUtil.VerifyDacDll(libraryCopy.FilePath, out IDisposable fileLock))
                    {
                        fileLock?.Dispose();
                        libraryCopy.Dispose();
                        Trace.TraceError($"RuntimeLibraryProvider: Authenticode verification failed for {path}");
                        return HResult.E_NOINTERFACE;
                    }
                    _verifiedFiles.Add(fileLock);
                    _libraryCopies.Add(libraryCopy);
                    path = libraryCopy.FilePath;
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
