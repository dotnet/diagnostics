// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public class Thread : IThread, IDisposable
    {
        private readonly ThreadService _threadService;
        private ReadOnlyMemory<byte> _threadContext;
        private ulong? _teb;

        protected readonly ServiceContainer _serviceContainer;

        public Thread(ThreadService threadService, int index, uint id)
        {
            _threadService = threadService;
            ThreadIndex = index;
            ThreadId = id;
            _serviceContainer = threadService.Services.GetService<IServiceManager>().CreateServiceContainer(ServiceScope.Thread, threadService.Services);
            _serviceContainer.AddService<IThread>(this);
        }

        void IDisposable.Dispose()
        {
            _serviceContainer.RemoveService(typeof(IThread));
            _serviceContainer.DisposeServices();
        }

        #region IThread

        public int ThreadIndex { get; }

        public uint ThreadId { get; }

        public ITarget Target => _threadService.Target;

        public IServiceProvider Services => _serviceContainer;

        public bool TryGetRegisterValue(int registerIndex, out ulong value)
        {
            try
            {
                ReadOnlySpan<byte> context = GetThreadContext();
                return _threadService.TryGetRegisterValue(context, registerIndex, out value);
            }
            catch (DiagnosticsException ex)
            {
                Trace.TraceError($"GetRegisterValue: 0x{ThreadId:X4} {ex}");
            }
            value = 0;
            return false;
        }

        public ReadOnlySpan<byte> GetThreadContext()
        {
            if (_threadContext.IsEmpty)
            {
                _threadContext = _threadService.GetThreadContext(this);
            }
            return _threadContext.Span;
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

        public override bool Equals(object obj)
        {
            IThread thread = (IThread)obj;
            return Target == thread.Target && ThreadId == thread.ThreadId;
        }

        public override int GetHashCode()
        {
            return Utilities.CombineHashCodes(Target.GetHashCode(), ThreadId.GetHashCode());
        }

        public override string ToString()
        {
            return $"#{ThreadIndex} {ThreadId:X8}";
        }
    }
}
