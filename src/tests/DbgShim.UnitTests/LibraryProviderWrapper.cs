// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using Xunit;

namespace SOS.Hosting
{
    public sealed unsafe class LibraryProviderWrapper : COMCallableIUnknown, IHost
    {
        public enum LIBRARY_PROVIDER_INDEX_TYPE
        {
            Unknown = 0,
            Identity = 1,
            Runtime = 2,
        };

        public static readonly Guid IID_ICLRDebuggingLibraryProvider = new Guid("3151C08D-4D09-4f9b-8838-2880BF18FE51");
        public static readonly Guid IID_ICLRDebuggingLibraryProvider2 = new Guid("E04E2FF1-DCFD-45D5-BCD1-16FFF2FAF7BA");
        public static readonly Guid IID_ICLRDebuggingLibraryProvider3 = new Guid("DE3AAB18-46A0-48B4-BF0D-2C336E69EA1B");

        public IntPtr ILibraryProvider { get; }

        private readonly OSPlatform _targetOS;
        private readonly ImmutableArray<byte> _runtimeModuleBuildId;
        private readonly string _dbiModulePath;
        private readonly string _dacModulePath;
        private ISymbolService _symbolService;

        public LibraryProviderWrapper(string runtimeModulePath, string dbiModulePath, string dacModulePath)
           : this(GetRunningOS(), GetBuildId(runtimeModulePath), dbiModulePath, dacModulePath)
        {
        }

        public LibraryProviderWrapper(OSPlatform targetOS, ImmutableArray<byte> runtimeModuleBuildId, string dbiModulePath, string dacModulePath)
        {
            _targetOS = targetOS;
            _runtimeModuleBuildId = runtimeModuleBuildId;
            _dbiModulePath = dbiModulePath;
            _dacModulePath = dacModulePath;

            VTableBuilder builder = AddInterface(IID_ICLRDebuggingLibraryProvider, validate: false);
            builder.AddMethod(new ProvideLibraryDelegate(ProvideLibrary));
            ILibraryProvider = builder.Complete();

            builder = AddInterface(IID_ICLRDebuggingLibraryProvider2, validate: false);
            builder.AddMethod(new ProvideLibrary2Delegate(ProvideLibrary2));
            builder.Complete();

            builder = AddInterface(IID_ICLRDebuggingLibraryProvider3, validate: false);
            builder.AddMethod(new ProvideWindowsLibraryDelegate(ProvideWindowsLibrary));
            builder.AddMethod(new ProvideUnixLibraryDelegate(ProvideUnixLibrary));
            builder.Complete();

            AddRef();
        }

        private static OSPlatform GetRunningOS()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return OSPlatform.Windows;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return OSPlatform.Linux;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return OSPlatform.OSX;
            }

            throw new NotSupportedException($"OS not supported {RuntimeInformation.OSDescription}");
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("LibraryProviderWrapper.Destroy");
        }

        private int ProvideLibrary(
            IntPtr self,
            string fileName,
            uint timeStamp,
            uint sizeOfImage,
            out IntPtr moduleHandle)
        {
            Trace.TraceInformation($"LibraryProviderWrapper.ProvideLibrary {fileName} {timeStamp:X8} {sizeOfImage:X8}");
            try
            {
                // This should only be called when hosted on Windows because of the PAL module handle problems
                Assert.True(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

                string modulePath = null;
                if (fileName.Contains(DbiName))
                {
                    if (_dbiModulePath is not null)
                    {
                        modulePath = _dbiModulePath;
                    }
                    else
                    {
                        modulePath = DownloadModule(DbiName, timeStamp, sizeOfImage);
                    }
                }
                // This needs to work for long named DAC's so remove the extension
                else if (fileName.Contains(Path.GetFileNameWithoutExtension(DacName)))
                {
                    if (_dacModulePath is not null)
                    {
                        modulePath = _dacModulePath;
                    }
                    else
                    {
                        modulePath = DownloadModule(DacName, timeStamp, sizeOfImage);
                    }
                }
                TestGetPEInfo(modulePath, timeStamp, sizeOfImage);
                moduleHandle = DataTarget.PlatformFunctions.LoadLibrary(modulePath);
                Trace.TraceInformation($"LibraryProviderWrapper.ProvideLibrary SUCCEEDED {modulePath}");
                return HResult.S_OK;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"LibraryProviderWrapper.ProvideLibrary {ex}");
            }
            Trace.TraceError($"LibraryProviderWrapper.ProvideLibrary FAILED");
            moduleHandle = IntPtr.Zero;
            return HResult.E_INVALIDARG;
        }

        private int ProvideLibrary2(
            IntPtr self,
            string fileName,
            uint timeStamp,
            uint sizeOfImage,
            out IntPtr modulePathOut)
        {
            Trace.TraceInformation($"LibraryProviderWrapper.ProvideLibrary2 {fileName} {timeStamp:X8} {sizeOfImage:X8}");
            try
            {
                string modulePath = null;
                if (fileName.Contains(DbiName))
                {
                    if (_dbiModulePath != null)
                    {
                        modulePath = _dbiModulePath;
                    }
                    else
                    {
                        modulePath = DownloadModule(DbiName, timeStamp, sizeOfImage);
                    }
                }
                // This needs to work for long named DAC's so remove the extension
                else if (fileName.Contains(Path.GetFileNameWithoutExtension(DacName)))
                {
                    if (_dacModulePath != null)
                    {
                        modulePath = _dacModulePath;
                    }
                    else
                    {
                        modulePath = DownloadModule(DacName, timeStamp, sizeOfImage);
                    }
                }
                // If this is called on Linux or MacOS don't verify. This should only happen if
                // these tests are run against an old dbgshim version.
                if (_targetOS == OSPlatform.Windows)
                {
                    TestGetPEInfo(modulePath, timeStamp, sizeOfImage);
                }
                modulePathOut = Marshal.StringToCoTaskMemUni(modulePath);
                Trace.TraceInformation($"LibraryProviderWrapper.ProvideLibrary2 SUCCEEDED {modulePath}");
                return HResult.S_OK;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"LibraryProviderWrapper.ProvideLibrary2 {ex}");
            }
            Trace.TraceError("LibraryProviderWrapper.ProvideLibrary2 FAILED");
            modulePathOut = IntPtr.Zero;
            return HResult.E_INVALIDARG;
        }

        private int ProvideWindowsLibrary(
            IntPtr self,
            string fileName,
            string runtimeModulePath,
            LIBRARY_PROVIDER_INDEX_TYPE indexType,
            uint timeStamp,
            uint sizeOfImage,
            out IntPtr modulePathOut)
        {
            Trace.TraceInformation($"LibraryProviderWrapper.ProvideWindowsLibrary {fileName} {runtimeModulePath} {timeStamp:X8} {sizeOfImage:X8}");
            try
            {
                // Should only be called for Windows targets
                Assert.Equal(OSPlatform.Windows, _targetOS);

                // Should always get the identity on Windows
                Assert.Equal(LIBRARY_PROVIDER_INDEX_TYPE.Identity, indexType);

                string modulePath = null;
                if (fileName.Contains(DbiName))
                {
                    if (_dbiModulePath != null)
                    {
                        modulePath = _dbiModulePath;
                    }
                    else
                    {
                        modulePath = DownloadModule(DbiName, timeStamp, sizeOfImage);
                    }
                }
                // This needs to work for long named DAC's so remove the extension
                else if (fileName.Contains(Path.GetFileNameWithoutExtension(DacName)))
                {
                    if (_dacModulePath != null)
                    {
                        modulePath = _dacModulePath;
                    }
                    else
                    {
                        modulePath = DownloadModule(DacName, timeStamp, sizeOfImage);
                    }
                }
                TestGetPEInfo(modulePath, timeStamp, sizeOfImage);
                modulePathOut = Marshal.StringToCoTaskMemUni(modulePath);
                Trace.TraceInformation($"LibraryProviderWrapper.ProvideWindowsLibrary SUCCEEDED {modulePath}");
                return HResult.S_OK;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"LibraryProviderWrapper.ProvideWindowsLibrary {ex}");
            }
            Trace.TraceError("LibraryProviderWrapper.ProvideWindowsLibrary FAILED");
            modulePathOut = IntPtr.Zero;
            return HResult.E_INVALIDARG;
        }

        private int ProvideUnixLibrary(
            IntPtr self,
            string fileName,
            string runtimeModulePath,
            LIBRARY_PROVIDER_INDEX_TYPE indexType,
            byte* buildIdBytes,
            int buildIdSize,
            out IntPtr modulePathOut)
        {
            try
            {
                // Should only be called for Unix targets
                Assert.NotEqual(OSPlatform.Windows, _targetOS);

                byte[] buildId = Array.Empty<byte>();
                string modulePath = null;
                if (buildIdBytes != null && buildIdSize > 0)
                {
                    Span<byte> span = new Span<byte>(buildIdBytes, buildIdSize);
                    buildId = span.ToArray();
                }
                Trace.TraceInformation($"LibraryProviderWrapper.ProvideUnixLibrary {fileName} {runtimeModulePath} {indexType} {string.Concat(buildId.Select((b) => b.ToString("x2")))}");
                if (fileName.Contains(DbiName))
                {
                    if (_dbiModulePath != null)
                    {
                        modulePath = _dbiModulePath;
                    }
                    else
                    {
                        modulePath = DownloadModule(DbiName, buildId);
                    }
                }
                else if (fileName.Contains(DacName))
                {
                    if (_dacModulePath != null)
                    {
                        modulePath = _dacModulePath;
                    }
                    else
                    {
                        modulePath = DownloadModule(DacName, buildId);
                    }
                }
                if (modulePath != null)
                {
                    switch (indexType)
                    {
                        case LIBRARY_PROVIDER_INDEX_TYPE.Identity:
                            TestBuildId(GetBuildId(modulePath), buildId);
                            break;

                        case LIBRARY_PROVIDER_INDEX_TYPE.Runtime:
                            TestBuildId(_runtimeModuleBuildId, buildId);
                            break;
                    }
                    modulePathOut = Marshal.StringToCoTaskMemUni(modulePath);
                    Trace.TraceInformation($"LibraryProviderWrapper.ProvideUnixLibrary SUCCEEDED {modulePath}");
                    return HResult.S_OK;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"LibraryProviderWrapper.ProvideUnixLibrary {ex}");
            }
            Trace.TraceError("LibraryProviderWrapper.ProvideUnixLibrary FAILED");
            modulePathOut = IntPtr.Zero;
            return HResult.E_INVALIDARG;
        }

        private string DownloadModule(string moduleName, uint timeStamp, uint sizeOfImage)
        {
            Assert.True(timeStamp != 0 && sizeOfImage != 0);
            SymbolStoreKey key = PEFileKeyGenerator.GetKey(moduleName, timeStamp, sizeOfImage);
            Assert.NotNull(key);
            string downloadedPath = SymbolService.DownloadFile(key);
            Assert.NotNull(downloadedPath);
            return downloadedPath;
        }

        private string DownloadModule(string moduleName, byte[] buildId)
        {
            Assert.True(buildId.Length > 0);
            SymbolStoreKey key = null;
            OSPlatform platform;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // This is the cross-DAC case when OpenVirtualProcess calls on a Linux/MacOS dump. Should never
                // get here for a Windows dump or for live sessions (RegisterForRuntimeStartup, etc).
                platform = _targetOS;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                platform = OSPlatform.Linux;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                platform = OSPlatform.OSX;
            }
            else
            {
                throw new NotSupportedException($"OS not supported {RuntimeInformation.OSDescription}");
            }
            if (platform == OSPlatform.Linux)
            {
                key = ELFFileKeyGenerator.GetKeys(KeyTypeFlags.IdentityKey, moduleName, buildId, symbolFile: false, symbolFileName: null).SingleOrDefault();
            }
            else if (platform == OSPlatform.OSX)
            {
                key = MachOFileKeyGenerator.GetKeys(KeyTypeFlags.IdentityKey, moduleName, buildId, symbolFile: false, symbolFileName: null).SingleOrDefault();
            }
            Assert.NotNull(key);
            string downloadedPath = SymbolService.DownloadFile(key);
            Assert.NotNull(downloadedPath);
            return downloadedPath;
        }

        private void TestGetPEInfo(string filePath, uint timeStamp, uint sizeOfImage)
        {
            if (filePath != null && timeStamp != 0 && sizeOfImage != 0)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using Stream stream = Utilities.TryOpenFile(filePath);
                    if (stream is not null)
                    {
                        var peFile = new PEFile(new StreamAddressSpace(stream), false);
                        if (peFile.IsValid())
                        {
                            Assert.Equal(peFile.Timestamp, timeStamp);
                            Assert.Equal(peFile.SizeOfImage, sizeOfImage);
                            return;
                        }
                    }
                    throw new ArgumentException($"GetPEInfo {filePath} not valid PE file");
                }
            }
        }

        private void TestBuildId(ImmutableArray<byte> expectedBuildId, byte[] actualBuildId)
        {
            if (expectedBuildId.Length > 0)
            {
                Assert.Equal(expectedBuildId, actualBuildId);
            }
        }

        private static ImmutableArray<byte> GetBuildId(string filePath)
        {
            if (filePath != null)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    using ELFFile elfFile = Utilities.OpenELFFile(filePath);
                    if (elfFile is not null)
                    {
                        return elfFile.BuildID.ToImmutableArray();
                    }
                    throw new ArgumentException($"TestBuildId {filePath} not valid ELF file");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    using MachOFile machOFile = Utilities.OpenMachOFile(filePath);
                    if (machOFile is not null)
                    {
                        return machOFile.Uuid.ToImmutableArray();
                    }
                    throw new ArgumentException($"TestBuildId {filePath} not valid MachO file");
                }
            }
            return ImmutableArray<byte>.Empty;
        }

        private string DbiName
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "mscordbi.dll";
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return "libmscordbi.so";
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return "libmscordbi.dylib";
                }

                throw new NotSupportedException($"OS not supported {RuntimeInformation.OSDescription}");
            }
        }

        private string DacName
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "mscordaccore.dll";
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return "libmscordaccore.so";
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return "libmscordaccore.dylib";
                }

                throw new NotSupportedException($"OS not supported {RuntimeInformation.OSDescription}");
            }
        }

        private ISymbolService SymbolService
        {
            get
            {
                if (_symbolService is null)
                {
                    _symbolService = new SymbolService(this);
                    _symbolService.AddSymbolServer(msdl: true, symweb: false, timeoutInMinutes: 6, retryCount: 5);
                    _symbolService.AddCachePath(SymbolService.DefaultSymbolCache);
                }
                return _symbolService;
            }
        }

        #region IHost

        IServiceEvent IHost.OnShutdownEvent => throw new NotImplementedException();

        IServiceEvent<ITarget> IHost.OnTargetCreate => throw new NotImplementedException();

        HostType IHost.HostType => HostType.DotnetDump;

        IServiceProvider IHost.Services => throw new NotImplementedException();

        IEnumerable<ITarget> IHost.EnumerateTargets() => throw new NotImplementedException();

        #endregion

        #region ICLRDebuggingLibraryProvider* delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ProvideLibraryDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPWStr)] string fileName,
            [In] uint timeStamp,
            [In] uint sizeOfImage,
            out IntPtr moduleHandle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ProvideLibrary2Delegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPWStr)] string fileName,
            [In] uint timeStamp,
            [In] uint sizeOfImage,
            out IntPtr modulePath);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ProvideWindowsLibraryDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPWStr)] string fileName,
            [In, MarshalAs(UnmanagedType.LPWStr)] string runtimeModulePath,
            [In] LIBRARY_PROVIDER_INDEX_TYPE indexType,
            [In] uint timeStamp,
            [In] uint sizeOfImage,
            out IntPtr modulePath);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ProvideUnixLibraryDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPWStr)] string fileName,
            [In, MarshalAs(UnmanagedType.LPWStr)] string runtimeModulePath,
            [In] LIBRARY_PROVIDER_INDEX_TYPE indexType,
            [In] byte* buildIdBytes,
            [In] int buildIdSize,
            out IntPtr modulePath);

        #endregion
    }
}
