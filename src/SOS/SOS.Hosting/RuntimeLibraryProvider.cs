// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
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
        private static readonly string[] s_requiredCertificateOids =
            ["1.3.6.1.4.1.311.84.4.1"];
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
                    if (!VerifySignature(path, s_requiredCertificateOids, out IDisposable fileLock))
                    {
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

        internal static unsafe bool VerifySignature(
            string path,
            IReadOnlyCollection<string> requiredCertificateOids,
            out IDisposable fileLock)
        {
            fileLock = null;
            FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            WINTRUST_FILE_INFO trustInfo = new()
            {
                cbStruct = (uint)sizeof(WINTRUST_FILE_INFO),
                hFile = stream.SafeFileHandle.DangerousGetHandle(),
            };
            WINTRUST_DATA trustData = new()
            {
                cbStruct = (uint)sizeof(WINTRUST_DATA),
                dwUIChoice = 2,
                dwUnionChoice = 1,
                pFile = new IntPtr(&trustInfo),
                dwStateAction = 1,
                dwProvFlags = 0x1040,
            };
            Guid action = new(0xaac56b, 0xcd44, 0x11d0, 0x8c, 0xc2, 0x0, 0xc0, 0x4f, 0xc2, 0x95, 0xee);
            int result = WinVerifyTrust(IntPtr.Zero, &action, &trustData);
            try
            {
                if (result != 0)
                {
                    return false;
                }

                IntPtr provider = WTHelperProvDataFromStateData(trustData.hWVTStateData);
                CRYPT_PROVIDER_SGNR* signer = provider != IntPtr.Zero
                    ? WTHelperGetProvSignerFromChain(provider, 0, false, 0)
                    : null;
                if (signer is null)
                {
                    return false;
                }

                CERT_CHAIN_POLICY_PARA policyParameters = new()
                {
                    cbSize = (uint)sizeof(CERT_CHAIN_POLICY_PARA)
                };
                CERT_CHAIN_POLICY_STATUS policyStatus = new()
                {
                    cbSize = (uint)sizeof(CERT_CHAIN_POLICY_STATUS)
                };
                if (!CertVerifyCertificateChainPolicy(
                    new IntPtr(7),
                    signer->pChainContext,
                    &policyParameters,
                    &policyStatus) ||
                    policyStatus.dwError != 0)
                {
                    return false;
                }

                CRYPT_PROVIDER_CERT* leaf = WTHelperGetProvCertFromChain(signer, 0);
                if (leaf is null)
                {
                    return false;
                }

                using X509Certificate2 certificate = new(leaf->pCert);
                bool validOid = certificate.Extensions
                    .OfType<X509EnhancedKeyUsageExtension>()
                    .SelectMany(extension => extension.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>())
                    .Any(oid => requiredCertificateOids.Contains(oid.Value));
                if (!validOid)
                {
                    return false;
                }

                fileLock = stream;
                stream = null;
                return true;
            }
            finally
            {
                trustData.dwStateAction = 2;
                WinVerifyTrust(IntPtr.Zero, &action, &trustData);
                stream?.Dispose();
            }
        }

        [DllImport("wintrust.dll")]
        private static extern unsafe int WinVerifyTrust(IntPtr hwnd, Guid* action, WINTRUST_DATA* trustData);

        [DllImport("wintrust.dll")]
        private static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

        [DllImport("wintrust.dll")]
        private static extern unsafe CRYPT_PROVIDER_SGNR* WTHelperGetProvSignerFromChain(
            IntPtr provider,
            uint signer,
            [MarshalAs(UnmanagedType.Bool)] bool counterSigner,
            uint counterSignerIndex);

        [DllImport("wintrust.dll")]
        private static extern unsafe CRYPT_PROVIDER_CERT* WTHelperGetProvCertFromChain(
            CRYPT_PROVIDER_SGNR* signer,
            uint certificateIndex);

        [DllImport("crypt32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern unsafe bool CertVerifyCertificateChainPolicy(
            IntPtr policy,
            IntPtr chainContext,
            CERT_CHAIN_POLICY_PARA* policyParameters,
            CERT_CHAIN_POLICY_STATUS* policyStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPT_PROVIDER_SGNR
        {
            public uint cbStruct;
            public uint lowDateTime;
            public uint highDateTime;
            public uint certificateChainCount;
            public IntPtr certificateChain;
            public uint signerType;
            public IntPtr signer;
            public uint error;
            public uint counterSignerCount;
            public IntPtr counterSigners;
            public IntPtr pChainContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPT_PROVIDER_CERT
        {
            public uint cbStruct;
            public IntPtr pCert;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_CHAIN_POLICY_STATUS
        {
            public uint cbSize;
            public uint dwError;
            public int chainIndex;
            public int elementIndex;
            public IntPtr extraPolicyStatus;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_CHAIN_POLICY_PARA
        {
            public uint cbSize;
            public uint flags;
            public IntPtr extraPolicyParameters;
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
