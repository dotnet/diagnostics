// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

        public void Dispose()
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
                byte[] threadContext = new byte[_threadService.ContextSize];
                if (!GetThreadContextInner(_threadService.ContextFlags, threadContext))
                {
                    throw new DiagnosticsException($"Unable to get the context for thread {ThreadId:X8} with flags {_threadService.ContextFlags:X8}");
                }
                _threadContext = threadContext;
            }
            return _threadContext.Span;
        }

        /// <summary>
        /// Get the thread context
        /// </summary>
        /// <param name="contextFlags">Windows context flags</param>
        /// <param name="context">Context buffer</param>
        /// <returns>true succeeded, false failed</returns>
        protected virtual bool GetThreadContextInner(uint contextFlags, byte[] context) => _threadService.GetThreadContext(ThreadId, contextFlags, context);

        public ulong GetThreadTeb()
        {
            if (!_teb.HasValue)
            {
                _teb = GetThreadTebInner();
            }
            return _teb.Value;
        }

        /// <summary>
        /// Returns the Windows TEB pointer for the thread
        /// </summary>
        /// <returns>TEB pointer or 0 if not implemented or thread id not found</returns>
        protected virtual ulong GetThreadTebInner() => _threadService.GetThreadTeb(ThreadId);

        #endregion

        protected void SetContextFlags(uint contextFlags, Span<byte> context)
        {
            Span<byte> threadSpan = context.Slice(_threadService.ContextFlagsOffset, sizeof(uint));
            MemoryMarshal.Write<uint>(threadSpan, ref contextFlags);
        }

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
