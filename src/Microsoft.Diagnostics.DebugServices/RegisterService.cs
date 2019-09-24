// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Architecture = Microsoft.Diagnostics.Runtime.Architecture;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Provides register info and values
    /// </summary>
    public class RegisterService
    {
        public struct RegisterInfo
        {
            public readonly int RegisterIndex;
            public readonly int RegisterOffset;
            public readonly int RegisterSize;
            public readonly string RegisterName;

            internal RegisterInfo(int registerIndex, int registerOffset, int registerSize, string registerName)
            {
                RegisterIndex = registerIndex;
                RegisterOffset = registerOffset;
                RegisterSize = registerSize;
                RegisterName = registerName;
            }
        }

        private readonly DataTarget _target;
        private readonly int _contextSize;
        private readonly uint _contextFlags;
        private readonly Dictionary<string, RegisterInfo> _lookupByName;
        private readonly Dictionary<int, RegisterInfo> _lookupByIndex;
        private readonly Dictionary<uint, byte[]> _threadContextCache = new Dictionary<uint, byte[]>();

        public IEnumerable<RegisterInfo> Registers { get; }

        public int InstructionPointerIndex { get; }

        public int FramePointerIndex { get; }

        public int StackPointerIndex { get; }

        public RegisterService(DataTarget target)
        {
            _target = target;

            Type contextType;
            switch (target.Architecture)
            {
                case Architecture.Amd64:
                    // Dumps generated with newer dbgeng have bigger context buffers and clrmd requires the context size to at least be that size.
                    _contextSize = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 0x700 : AMD64Context.Size;
                    _contextFlags = AMD64Context.ContextControl | AMD64Context.ContextInteger | AMD64Context.ContextSegments;
                    contextType = typeof(AMD64Context);
                    break;

                case Architecture.X86:
                    _contextSize = X86Context.Size;
                    _contextFlags = X86Context.ContextControl | X86Context.ContextInteger | X86Context.ContextSegments;
                    contextType = typeof(X86Context);
                    break;

                case Architecture.Arm64:
                    _contextSize = Arm64Context.Size;
                    _contextFlags = Arm64Context.ContextControl | Arm64Context.ContextInteger;
                    contextType = typeof(Arm64Context);
                    break;

                case Architecture.Arm:
                    _contextSize = ArmContext.Size;
                    _contextFlags = ArmContext.ContextControl | ArmContext.ContextInteger;
                    contextType = typeof(ArmContext);
                    break;

                default:
                    throw new PlatformNotSupportedException($"Unsupported architecture: {target.Architecture}");
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
                    InstructionPointerIndex = index;
                }
                if ((registerAttribute.RegisterType & RegisterType.StackPointer) != 0) {
                    StackPointerIndex = index;
                }
                if ((registerAttribute.RegisterType & RegisterType.FramePointer) != 0) {
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

        /// <summary>
        /// Return the register index for the register name
        /// </summary>
        /// <param name="name">register name</param>
        /// <param name="index">returns register index or -1</param>
        /// <returns>true if name found</returns>
        public bool GetRegisterIndexByName(string name, out int index)
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
        public bool GetRegisterInfo(int index, out RegisterInfo info)
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
        public bool GetRegisterValue(uint threadId, int index, out ulong value)
        {
            value = 0;

            if (_lookupByIndex.TryGetValue(index, out RegisterInfo info))
            {
                byte[] threadContext = GetThreadContext(threadId);
                if (threadContext != null)
                {
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
            }
            return false;
        }

        /// <summary>
        /// Returns the raw context buffer bytes for the specified thread.
        /// </summary>
        /// <param name="threadId">thread id</param>
        /// <returns>register context or null if error</returns>
        public byte[] GetThreadContext(uint threadId)
        {
            if (_threadContextCache.TryGetValue(threadId, out byte[] threadContext))
            {
                return threadContext;
            }
            else
            {
                unsafe
                {
                    threadContext = new byte[_contextSize];
                    fixed (byte* ptr = threadContext)
                    {
                        try
                        {
                            if (_target.DataReader.GetThreadContext(threadId, _contextFlags, (uint)_contextSize, new IntPtr(ptr)))
                            {
                                _threadContextCache.Add(threadId, threadContext);
                                return threadContext;
                            }
                        }
                        catch (ClrDiagnosticsException ex)
                        {
                            Trace.TraceError(ex.ToString());
                        }
                    }
                }
            }
            return null;
        }
    }
}