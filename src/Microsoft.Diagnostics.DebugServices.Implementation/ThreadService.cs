// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;
using Architecture = System.Runtime.InteropServices.Architecture;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Provides thread and register info and values for the clrmd IDataReader
    /// </summary>
    public abstract class ThreadService : IThreadService, IDisposable
    {
        private readonly int _contextSize;
        private readonly uint _contextFlags;
        private readonly Dictionary<string, RegisterInfo> _lookupByName;
        private readonly Dictionary<int, RegisterInfo> _lookupByIndex;
        private Dictionary<uint, IThread> _threads;

        internal protected readonly IServiceProvider Services;
        internal protected readonly ITarget Target;

        public ThreadService(IServiceProvider services)
        {
            Services = services;
            Target = services.GetService<ITarget>();
            Target.OnFlushEvent.Register(Flush);

            Type contextType;
            switch (Target.Architecture)
            {
                case Architecture.X64:
                    // Dumps generated with newer dbgeng have bigger context buffers and clrmd requires the context size to at least be that size.
                    _contextSize = Target.OperatingSystem == OSPlatform.Windows ? 0x700 : AMD64Context.Size;
                    _contextFlags = AMD64Context.ContextControl | AMD64Context.ContextInteger | AMD64Context.ContextSegments | AMD64Context.ContextFloatingPoint;
                    contextType = typeof(AMD64Context);
                    break;

                case Architecture.X86:
                    _contextSize = X86Context.Size;
                    _contextFlags = X86Context.ContextControl | X86Context.ContextInteger | X86Context.ContextSegments | X86Context.ContextFloatingPoint;
                    contextType = typeof(X86Context);
                    break;

                case Architecture.Arm64:
                    _contextSize = Arm64Context.Size;
                    _contextFlags = Arm64Context.ContextControl | Arm64Context.ContextInteger | Arm64Context.ContextFloatingPoint;
                    contextType = typeof(Arm64Context);
                    break;

                case Architecture.Arm:
                    _contextSize = ArmContext.Size;
                    _contextFlags = ArmContext.ContextControl | ArmContext.ContextInteger | ArmContext.ContextFloatingPoint;
                    contextType = typeof(ArmContext);
                    break;

                default:
                    throw new PlatformNotSupportedException($"Unsupported architecture: {Target.Architecture}");
            }

            var registers = new List<RegisterInfo>();
            int index = 0;

            FieldInfo[] fields = contextType.GetFields(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo field in fields)
            {
                RegisterAttribute registerAttribute = field.GetCustomAttributes<RegisterAttribute>(inherit: false).SingleOrDefault();
                if (registerAttribute is null)
                {
                    continue;
                }
                RegisterType registerType = registerAttribute.RegisterType & RegisterType.TypeMask;
                switch (registerType)
                {
                    case RegisterType.Control:
                    case RegisterType.General:
                    case RegisterType.Segments:
                        break;
                    default:
                        continue;
                }
                if ((registerAttribute.RegisterType & RegisterType.ProgramCounter) != 0)
                {
                    InstructionPointerIndex = index;
                }
                if ((registerAttribute.RegisterType & RegisterType.StackPointer) != 0)
                {
                    StackPointerIndex = index;
                }
                if ((registerAttribute.RegisterType & RegisterType.FramePointer) != 0)
                {
                    FramePointerIndex = index;
                }
                FieldOffsetAttribute offsetAttribute = field.GetCustomAttributes<FieldOffsetAttribute>(inherit: false).Single();
                var registerInfo = new RegisterInfo(index, offsetAttribute.Value, Marshal.SizeOf(field.FieldType), registerAttribute.Name ?? field.Name.ToLower());
                registers.Add(registerInfo);
                index++;
            }

            _lookupByName = registers.ToDictionary((info) => info.RegisterName);
            _lookupByIndex = registers.ToDictionary((info) => info.RegisterIndex);
            Registers = registers;
        }

        void IDisposable.Dispose() => Flush();

        private void Flush()
        {
            if (_threads is not null)
            {
                foreach (IThread thread in _threads.Values)
                {
                    if (thread is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                _threads.Clear();
                _threads = null;
            }
        }

        #region IThreadService

        /// <summary>
        /// Details on all the supported registers
        /// </summary>
        public IEnumerable<RegisterInfo> Registers { get; }

        /// <summary>
        /// The instruction pointer register index
        /// </summary>
        public int InstructionPointerIndex { get; }

        /// <summary>
        /// The frame pointer register index
        /// </summary>
        public int FramePointerIndex { get; }

        /// <summary>
        /// The stack pointer register index
        /// </summary>
        public int StackPointerIndex { get; }

        /// <summary>
        /// Return the register index for the register name
        /// </summary>
        /// <param name="name">register name</param>
        /// <param name="index">returns register index or -1</param>
        /// <returns>true if name found</returns>
        public bool TryGetRegisterIndexByName(string name, out int index)
        {
            if (_lookupByName.TryGetValue(name, out RegisterInfo info))
            {
                index = info.RegisterIndex;
                return true;
            }
            index = int.MaxValue;
            return false;
        }

        /// <summary>
        /// Returns the register info (name, offset, size, etc).
        /// </summary>
        /// <param name="index">register index</param>
        /// <param name="info">RegisterInfo</param>
        /// <returns>true if index found</returns>
        public bool TryGetRegisterInfo(int index, out RegisterInfo info)
        {
            return _lookupByIndex.TryGetValue(index, out info);
        }

        /// <summary>
        /// Enumerate all the native threads
        /// </summary>
        /// <returns>ThreadInfos for all the threads</returns>
        public IEnumerable<IThread> EnumerateThreads()
        {
            return GetThreads().OrderBy((pair) => pair.Value.ThreadIndex).Select((pair) => pair.Value);
        }

        /// <summary>
        /// Get the thread info from the thread index
        /// </summary>
        /// <param name="threadIndex">index</param>
        /// <returns>thread info</returns>
        /// <exception cref="DiagnosticsException">invalid thread index</exception>
        public IThread GetThreadFromIndex(int threadIndex)
        {
            try
            {
                return GetThreads().First((pair) => pair.Value.ThreadIndex == threadIndex).Value;
            }
            catch (InvalidOperationException ex)
            {
                throw new DiagnosticsException($"Invalid thread index: {threadIndex}", ex);
            }
        }

        /// <summary>
        /// Get the thread info from the OS thread id
        /// </summary>
        /// <param name="threadId">os id</param>
        /// <returns>thread info</returns>
        /// <exception cref="DiagnosticsException">invalid thread id</exception>
        public IThread GetThreadFromId(uint threadId)
        {
            if (!GetThreads().TryGetValue(threadId, out IThread thread))
            {
                throw new DiagnosticsException($"Invalid thread id: {threadId}");
            }
            return thread;
        }

        #endregion

        /// <summary>
        /// Get the thread context
        /// </summary>
        /// <param name="thread">thread instance</param>
        /// <returns>context array</returns>
        internal byte[] GetThreadContext(Thread thread)
        {
            var threadContext = new byte[_contextSize];
            if (!GetThreadContext(thread.ThreadId, _contextFlags, (uint)_contextSize, threadContext))
            {
                throw new DiagnosticsException();
            }
            return threadContext;
        }

        /// <summary>
        /// Get the thread TEB
        /// </summary>
        /// <param name="thread">thread instance</param>
        /// <returns>TEB</returns>
        internal ulong GetThreadTeb(Thread thread)
        {
            return GetThreadTeb(thread.ThreadId);
        }

        /// <summary>
        /// Get/create the thread dictionary.
        /// </summary>
        private Dictionary<uint, IThread> GetThreads()
        {
            _threads ??= GetThreadsInner().OrderBy((thread) => thread.ThreadId).ToDictionary((thread) => thread.ThreadId);
            return _threads;
        }

        /// <summary>
        /// Get/creates the threads.
        /// </summary>
        protected abstract IEnumerable<IThread> GetThreadsInner();

        /// <summary>
        /// Get the thread context
        /// </summary>
        /// <param name="threadId">OS thread id</param>
        /// <param name="contextFlags">Windows context flags</param>
        /// <param name="contextSize">Context size</param>
        /// <param name="context">Context buffer</param>
        /// <returns>true succeeded, false failed</returns>
        /// <exception cref="DiagnosticsException">invalid thread id</exception>
        protected abstract bool GetThreadContext(uint threadId, uint contextFlags, uint contextSize, byte[] context);

        /// <summary>
        /// Returns the Windows TEB pointer for the thread
        /// </summary>
        /// <param name="threadId">OS thread id</param>
        /// <returns>TEB pointer or 0</returns>
        protected abstract ulong GetThreadTeb(uint threadId);
    }
}
