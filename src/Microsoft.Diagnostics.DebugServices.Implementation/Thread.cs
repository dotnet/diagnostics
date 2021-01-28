// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public class Thread : IThread
    {
        private readonly ThreadService _threadService;
        private byte[] _threadContext;
        private ulong? _teb;

        public readonly ServiceProvider ServiceProvider;

        public Thread(ThreadService threadService, int index, uint id)
        {
            _threadService = threadService;
            ThreadIndex = index;
            ThreadId = id;
            ServiceProvider = new ServiceProvider();
        }

        #region IThread

        public IServiceProvider Services => ServiceProvider;

        public int ThreadIndex { get; }

        public uint ThreadId { get; }

        public bool TryGetRegisterValue(int index, out ulong value)
        {
            value = 0;

            if (_threadService.TryGetRegisterInfo(index, out RegisterInfo info))
            {
                try
                {
                    Span<byte> threadContext = new Span<byte>(GetThreadContext(), info.RegisterOffset, info.RegisterSize);
                    switch (info.RegisterSize)
                    {
                        case 1:
                            value = MemoryMarshal.Read<byte>(threadContext);
                            return true;
                        case 2:
                            value = MemoryMarshal.Read<ushort>(threadContext);
                            return true;
                        case 4:
                            value = MemoryMarshal.Read<uint>(threadContext);
                            return true;
                        case 8:
                            value = MemoryMarshal.Read<ulong>(threadContext);
                            return true;
                        default:
                            Trace.TraceError($"GetRegisterValue: 0x{ThreadId:X4} {info.RegisterName} invalid size {info.RegisterSize}");
                            break;
                    }
                }
                catch (DiagnosticsException ex)
                {
                    Trace.TraceError($"GetRegisterValue: 0x{ThreadId:X4} {info.RegisterName} {ex}");
                }
            }
            return false;
        }

        public byte[] GetThreadContext()
        {
            if (_threadContext == null)
            {
                _threadContext = _threadService.GetThreadContext(this);
            }
            return _threadContext;
        }

        public ulong GetThreadTeb()
        {
            if (!_teb.HasValue)
            {
                _teb = _threadService.GetThreadTeb(this);
            }
            return _teb.Value;
        }

        #endregion

        public override string ToString()
        {
            return $"#{ThreadIndex} {ThreadId:X8}";
        }
    }
}
