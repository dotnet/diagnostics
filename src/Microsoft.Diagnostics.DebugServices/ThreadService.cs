// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.DataReaders.Implementation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Architecture = Microsoft.Diagnostics.Runtime.Architecture;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Provides thread and register info and values for the clrmd IDataReader
    /// </summary>
    public class ThreadService : IThreadService
    {
        private readonly IDataReader _dataReader;
        private readonly IThreadReader _threadReader;
        private readonly int _contextSize;
        private readonly uint _contextFlags;
        private readonly int _instructionPointerIndex;
        private readonly int _framePointerIndex;
        private readonly int _stackPointerIndex;
        private readonly Dictionary<string, RegisterInfo> _lookupByName;
        private readonly Dictionary<int, RegisterInfo> _lookupByIndex;
        private readonly IEnumerable<RegisterInfo> _registers;
        private readonly Dictionary<uint, byte[]> _threadContextCache = new Dictionary<uint, byte[]>();
        private IEnumerable<ThreadInfo> _threadInfos;

        public ThreadService(IDataReader dataReader)
        {
            _dataReader = dataReader;
            _threadReader = (IThreadReader)dataReader;

            Type contextType;
            switch (dataReader.Architecture)
            {
                case Architecture.Amd64:
                    // Dumps generated with newer dbgeng have bigger context buffers and clrmd requires the context size to at least be that size.
                    _contextSize = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 0x700 : AMD64Context.Size;
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
                    throw new PlatformNotSupportedException($"Unsupported architecture: {dataReader.Architecture}");
            }

            var registers = new List<RegisterInfo>();
            int index = 0;

            FieldInfo[] fields = contextType.GetFields(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo field in fields) {
                RegisterAttribute registerAttribute = field.GetCustomAttributes<RegisterAttribute>(inherit: false).SingleOrDefault();
                if (registerAttribute == null) {
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
                if ((registerAttribute.RegisterType & RegisterType.ProgramCounter) != 0) {
                    _instructionPointerIndex = index;
                }
                if ((registerAttribute.RegisterType & RegisterType.StackPointer) != 0) {
                    _stackPointerIndex = index;
                }
                if ((registerAttribute.RegisterType & RegisterType.FramePointer) != 0) {
                    _framePointerIndex = index;
                }
                FieldOffsetAttribute offsetAttribute = field.GetCustomAttributes<FieldOffsetAttribute>(inherit: false).Single();
                var registerInfo = new RegisterInfo(index, offsetAttribute.Value, Marshal.SizeOf(field.FieldType), registerAttribute.Name ?? field.Name.ToLower());
                registers.Add(registerInfo);
                index++;
            }

            _lookupByName = registers.ToDictionary((info) => info.RegisterName);
            _lookupByIndex = registers.ToDictionary((info) => info.RegisterIndex);
            _registers = registers;
        }

        /// <summary>
        /// Flush the register service
        /// </summary>
        public void Flush()
        {
            _threadContextCache.Clear();
        }

        /// <summary>
        /// Details on all the supported registers
        /// </summary>
        IEnumerable<RegisterInfo> IThreadService.Registers { get { return _registers; } }

        /// <summary>
        /// The instruction pointer register index
        /// </summary>
        int IThreadService.InstructionPointerIndex { get { return _instructionPointerIndex; } }

        /// <summary>
        /// The frame pointer register index
        /// </summary>
        int IThreadService.FramePointerIndex { get { return _framePointerIndex; } }

        /// <summary>
        /// The stack pointer register index
        /// </summary>
        int IThreadService.StackPointerIndex { get { return _stackPointerIndex; } }

        /// <summary>
        /// Return the register index for the register name
        /// </summary>
        /// <param name="name">register name</param>
        /// <param name="index">returns register index or -1</param>
        /// <returns>true if name found</returns>
        bool IThreadService.GetRegisterIndexByName(string name, out int index)
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
        bool IThreadService.GetRegisterInfo(int index, out RegisterInfo info)
        {
            return _lookupByIndex.TryGetValue(index, out info);
        }

        /// <summary>
        /// Returns the register value for the thread and register index. This function
        /// can only return register values that are 64 bits or less and currently the
        /// clrmd data targets don't return any floating point or larger registers.
        /// </summary>
        /// <param name="threadId">thread id</param>
        /// <param name="index">register index</param>
        /// <param name="value">value returned</param>
        /// <returns>true if value found</returns>
        bool IThreadService.GetRegisterValue(uint threadId, int index, out ulong value)
        {
            value = 0;

            if (_lookupByIndex.TryGetValue(index, out RegisterInfo info))
            {
                try 
                { 
                    byte[] threadContext = ((IThreadService)this).GetThreadContext(threadId);
                    unsafe
                    {
                        fixed (byte* ptr = threadContext)
                        {
                            switch (info.RegisterSize)
                            {
                                case 1:
                                    value = *((byte*)(ptr + info.RegisterOffset));
                                    return true;
                                case 2:
                                    value = *((ushort*)(ptr + info.RegisterOffset));
                                    return true;
                                case 4:
                                    value = *((uint*)(ptr + info.RegisterOffset));
                                    return true;
                                case 8:
                                    value = *((ulong*)(ptr + info.RegisterOffset));
                                    return true;
                            }
                        }
                    }
                }
                catch (DiagnosticsException)
                {
                }
            }
            return false;
        }

        /// <summary>
        /// Returns the raw context buffer bytes for the specified thread.
        /// </summary>
        /// <param name="threadId">thread id</param>
        /// <returns>register context</returns>
        /// <exception cref="DiagnosticsException">invalid thread id</exception>
        byte[] IThreadService.GetThreadContext(uint threadId)
        {
            if (_threadContextCache.TryGetValue(threadId, out byte[] threadContext))
            {
                return threadContext;
            }
            else
            {
                threadContext = new byte[_contextSize];
                try
                {
                    if (_dataReader.GetThreadContext(threadId, _contextFlags, new Span<byte>(threadContext, 0, _contextSize)))
                    {
                        _threadContextCache.Add(threadId, threadContext);
                        return threadContext;
                    }
                }
                catch (ClrDiagnosticsException ex)
                {
                    throw new DiagnosticsException(ex.Message, ex);
                }
            }
            throw new DiagnosticsException();
        }

        /// <summary>
        /// Enumerate all the native threads
        /// </summary>
        /// <returns>ThreadInfos for all the threads</returns>
        IEnumerable<ThreadInfo> IThreadService.EnumerateThreads()
        {
            if (_threadInfos == null)
            {
                _threadInfos = _threadReader.EnumerateOSThreadIds()
                    .OrderBy((uint threadId) => threadId)
                    .Select((uint threadId, int threadIndex) => {
                        ulong teb = 0;
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            try
                            {
                                teb = _threadReader.GetThreadTeb(threadId);
                            }
                            catch (NotImplementedException)
                            {
                            }
                        }
                        return new ThreadInfo(threadIndex, threadId, teb); 
                    });
            }
            return _threadInfos;
        }

        /// <summary>
        /// Get the thread info from the thread index
        /// </summary>
        /// <param name="threadIndex">index</param>
        /// <returns>thread info</returns>
        /// <exception cref="DiagnosticsException">invalid thread index</exception>
        ThreadInfo IThreadService.GetThreadInfoFromIndex(int threadIndex)
        {
            try
            {
                return ((IThreadService)this).EnumerateThreads().ElementAt(threadIndex);
            }
            catch (ArgumentOutOfRangeException ex)
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
        ThreadInfo IThreadService.GetThreadInfoFromId(uint threadId)
        {
            try
            {
                return ((IThreadService)this).EnumerateThreads().First((ThreadInfo info) => info.ThreadId == threadId);
            }
            catch (InvalidOperationException ex)
            {
                throw new DiagnosticsException($"Invalid thread id: {threadId}", ex);
            }
        }
    }
}
