// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Diagnostics.Runtime.DbgEng;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    internal sealed unsafe class DacDataTarget
    {
        internal static readonly Guid IID_IDacDataTarget = new("3E11CCEE-D08B-43e5-AF01-32717A64DA03");
        internal static readonly Guid IID_IMetadataLocator = new("aa8fa804-bc05-4642-b2c5-c353ed22fc63");
        internal static readonly Guid IID_ICLRRuntimeLocator = new("b760bf44-9377-4597-8be7-58083bdc5146");

        public const ulong MagicCallbackConstant = 0x43;

        private readonly DataTarget _dataTarget;
        private readonly IDataReader _dataReader;
        private volatile ModuleInfo[]? _modules;

        private Action? _callback;
        private volatile int _callbackContext;

        public ulong RuntimeBaseAddress { get; }

        public DacDataTarget(DataTarget dataTarget, ulong runtimeBaseAddress = 0)
        {
            _dataTarget = dataTarget;
            _dataReader = _dataTarget.DataReader;
            RuntimeBaseAddress = runtimeBaseAddress;
        }

        public void EnterMagicCallbackContext() => Interlocked.Increment(ref _callbackContext);

        public void ExitMagicCallbackContext() => Interlocked.Decrement(ref _callbackContext);

        public void SetMagicCallback(Action flushCallback) => _callback = flushCallback;

        public IMAGE_FILE_MACHINE MachineType
        {
            get => _dataReader.Architecture switch
            {
                Architecture.X64 => IMAGE_FILE_MACHINE.AMD64,
                Architecture.X86 => IMAGE_FILE_MACHINE.I386,
                Architecture.Arm => IMAGE_FILE_MACHINE.THUMB2,
                Architecture.Arm64 => IMAGE_FILE_MACHINE.ARM64,
                _ => IMAGE_FILE_MACHINE.UNKNOWN,
            };
        }

        public int PointerSize => _dataTarget.DataReader.PointerSize;

        public void Flush()
        {
            _modules = null;
        }

        private ModuleInfo[] GetModules()
        {
            ModuleInfo[]? modules = _modules;
            if (modules is null)
            {
                modules = _dataTarget.EnumerateModules().ToArray();
                Array.Sort(modules, (left, right) => left.ImageBase.CompareTo(right.ImageBase));

                _modules = modules;
            }

            return modules;
        }

        private ModuleInfo? GetModule(ulong address)
        {
            ModuleInfo[] modules = GetModules();
            int min = 0, max = modules.Length - 1;

            while (min <= max)
            {
                int i = (min + max) / 2;
                ModuleInfo curr = modules[i];

                if (curr.ImageBase <= address && address < curr.ImageBase + (ulong)curr.IndexFileSize)
                    return curr;

                if (curr.ImageBase < address)
                    min = i + 1;
                else
                    max = i - 1;
            }

            return null;
        }

        public ulong GetImageBase(string imagePath)
        {
            imagePath = Path.GetFileNameWithoutExtension(imagePath);

            foreach (ModuleInfo module in GetModules())
            {
                string? moduleName = Path.GetFileNameWithoutExtension(module.FileName);
                if (imagePath.Equals(moduleName, StringComparison.CurrentCultureIgnoreCase) && module.ImageBase != 0)
                    return module.ImageBase;
            }

            return 0;
        }

        public bool ReadVirtual(ClrDataAddress cda, IntPtr buffer, int bytesRequested, out int bytesRead)
        {
            ulong address = cda;
            Span<byte> span = new(buffer.ToPointer(), bytesRequested);

            if (address == MagicCallbackConstant && _callbackContext > 0)
            {
                // See comment in RuntimeBuilder.FlushDac
                _callback?.Invoke();
                bytesRead = 0;
                return false;
            }

            int read = _dataReader.Read(address, span);
            if (read > 0)
            {
                bytesRead = read;
                return true;
            }

            bytesRead = 0;
            ModuleInfo? info = GetModule(address);
            if (info != null && info.FileName != null)
            {
                // We do not put a using statement here to prevent needing to load/unload the binary over and over.
                PEImage? peimage = _dataTarget.LoadPEImage(info.FileName, info.IndexTimeStamp, info.IndexFileSize, checkProperties: true, info.ImageBase);
                if (peimage != null)
                {
                    lock (peimage)
                    {
                        DebugOnly.Assert(peimage.IsValid);
                        int rva = checked((int)(address - info.ImageBase));
                        bytesRead = peimage.Read(rva, span);
                        if (bytesRead > 0)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public bool GetThreadContext(uint threadID, uint contextFlags, int contextSize, IntPtr context)
        {
            Span<byte> span = new(context.ToPointer(), contextSize);
            if (_dataReader.GetThreadContext(threadID, contextFlags, span))
                return true;

            Trace.TraceInformation($"Failed to read thread context: tid:{threadID:x} flags:{contextFlags:x} size:{contextSize:x}{(context == IntPtr.Zero ? " null context!" : "")}");
            return false;
        }

        public int GetMetadata(
            string fileName,
            int imageTimestamp,
            int imageSize,
            IntPtr mvid,
            uint mdRva,
            uint flags,
            uint bufferSize,
            IntPtr buffer,
            int* pDataSize)
        {
            if (buffer == IntPtr.Zero)
                return HResult.E_INVALIDARG;

            // We do not put a using statement here to prevent needing to load/unload the binary over and over.
            PEImage? peimage = _dataTarget.LoadPEImage(fileName, imageTimestamp, imageSize, checkProperties: true, imageBase: 0);
            if (peimage is null)
                return HResult.E_FAIL;

            int rva = (int)mdRva;
            int size = (int)bufferSize;
            if (rva == 0)
            {
                ImageDataDirectory metadata = peimage.MetadataDirectory;
                if (metadata.VirtualAddress == 0)
                    return HResult.E_FAIL;

                rva = metadata.VirtualAddress;
                size = Math.Min(size, metadata.Size);
            }

            checked
            {
                int read = peimage.Read(rva, new Span<byte>(buffer.ToPointer(), size));
                if (pDataSize != null)
                    *pDataSize = read;
            }

            return HResult.S_OK;
        }

        private delegate int GetMetadataDelegate(IntPtr self, [In][MarshalAs(UnmanagedType.LPWStr)] string fileName, int imageTimestamp, int imageSize,
                                                     IntPtr mvid, uint mdRva, uint flags, uint bufferSize, IntPtr buffer, int* dataSize);
        private delegate int GetMachineTypeDelegate(IntPtr self, out IMAGE_FILE_MACHINE machineType);
        private delegate int GetPointerSizeDelegate(IntPtr self, out int pointerSize);
        private delegate int GetImageBaseDelegate(IntPtr self, [In][MarshalAs(UnmanagedType.LPWStr)] string imagePath, out ulong baseAddress);
        private delegate int ReadVirtualDelegate(IntPtr self, ClrDataAddress address, IntPtr buffer, int bytesRequested, out int bytesRead);
        private delegate int WriteVirtualDelegate(IntPtr self, ClrDataAddress address, IntPtr buffer, uint bytesRequested, out uint bytesWritten);
        private delegate int GetTLSValueDelegate(IntPtr self, uint threadID, uint index, out ulong value);
        private delegate int SetTLSValueDelegate(IntPtr self, uint threadID, uint index, ClrDataAddress value);
        private delegate int GetCurrentThreadIDDelegate(IntPtr self, out uint threadID);
        private delegate int GetThreadContextDelegate(IntPtr self, uint threadID, uint contextFlags, int contextSize, IntPtr context);
        private delegate int SetThreadContextDelegate(IntPtr self, uint threadID, uint contextSize, IntPtr context);
        private delegate int RequestDelegate(IntPtr self, uint reqCode, uint inBufferSize, IntPtr inBuffer, IntPtr outBufferSize, out IntPtr outBuffer);

        private delegate int GetRuntimeBaseDelegate([In] IntPtr self, [Out] out ulong address);
    }
}