// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public class CrashInfoService : ICrashInfoService
    {
        /// <summary>
        /// This is a "transport" exception code required by Watson to trigger the proper analyzer/provider for bucketing
        /// </summary>
        public const uint STATUS_STACK_BUFFER_OVERRUN = 0xC0000409;

        /// <summary>
        /// This is the Native AOT fail fast subcode used by Watson
        /// </summary>
        public const uint FAST_FAIL_EXCEPTION_DOTNET_AOT = 0x48;

        private sealed class CrashInfoJson
        {
            [JsonPropertyName("version")]
            public string Version { get; set; }

            [JsonPropertyName("reason")]
            public int Reason { get; set; }

            [JsonPropertyName("runtime_type")]
            public int RuntimeType { get; set; }

            [JsonPropertyName("runtime_base")]
            [JsonConverter(typeof(HexUInt64Converter))]
            public ulong RuntimeBaseAddress { get; set; }

            [JsonPropertyName("runtime_version")]
            public string RuntimeVersion { get; set; }

            [JsonPropertyName("thread")]
            [JsonConverter(typeof(HexUInt32Converter))]
            public uint Thread { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; }

            [JsonPropertyName("exception")]
            public ExceptionJson Exception { get; set; }
        }

        private sealed class ExceptionJson
        {
            [JsonPropertyName("address")]
            [JsonConverter(typeof(HexUInt64Converter))]
            public ulong Address { get; set; }

            [JsonPropertyName("hr")]
            [JsonConverter(typeof(HexUInt32Converter))]
            public uint HResult { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; }

            [JsonPropertyName("type")]
            public string Type { get; set; }

            [JsonPropertyName("stack")]
            public StackFrameJson[] Stack { get; set; }

            [JsonPropertyName("inner")]
            public ExceptionJson[] InnerExceptions { get; set; }
        }

        private sealed class StackFrameJson
        {
            [JsonPropertyName("ip")]
            [JsonConverter(typeof(HexUInt64Converter))]
            public ulong InstructionPointer { get; set; }

            [JsonPropertyName("sp")]
            [JsonConverter(typeof(HexUInt64Converter))]
            public ulong StackPointer { get; set; }

            [JsonPropertyName("module")]
            [JsonConverter(typeof(HexUInt64Converter))]
            public ulong ModuleBase { get; set; }

            [JsonPropertyName("offset")]
            [JsonConverter(typeof(HexUInt32Converter))]
            public uint Offset { get; set; }

            [JsonPropertyName("name")]
            public string MethodName { get; set; }
        }

        private sealed class HexUInt64Converter : JsonConverter<ulong>
        {
            public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                string valueString = reader.GetString();
                if (valueString == null ||
                    !valueString.StartsWith("0x") ||
                    !ulong.TryParse(valueString.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong value))
                {
                    throw new JsonException("Invalid hex value");
                }
                return value;
            }

            public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options) => throw new NotImplementedException();
        }

        private sealed class HexUInt32Converter : JsonConverter<uint>
        {
            public override uint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                string valueString = reader.GetString();
                if (valueString == null ||
                    !valueString.StartsWith("0x") ||
                    !uint.TryParse(valueString.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint value))
                {
                    throw new JsonException("Invalid hex value");
                }
                return value;
            }

            public override void Write(Utf8JsonWriter writer, uint value, JsonSerializerOptions options) => throw new NotImplementedException();
        }

        private sealed class CrashInfoException : IException
        {
            private readonly ExceptionJson _exception;
            private readonly IModuleService _moduleService;
            private IStack _stack;
            private IEnumerable<IException> _inner;

            public CrashInfoException(ExceptionJson exception, IModuleService moduleService)
            {
                Debug.Assert(exception != null);
                Debug.Assert(moduleService != null);
                _exception = exception;
                _moduleService = moduleService;
            }

            public ulong Address => _exception.Address;

            public uint HResult => _exception.HResult;

            public string Message => _exception.Message;

            public string Type => _exception.Type;

            public IStack Stack => _stack ??= new CrashInfoStack(_exception.Stack, _moduleService);

            public IEnumerable<IException> InnerExceptions => _inner ??= _exception.InnerExceptions != null ? _exception.InnerExceptions.Select((inner) => new CrashInfoException(inner, _moduleService)) : Array.Empty<IException>();
        }

        private sealed class CrashInfoStack : IStack
        {
            private readonly IStackFrame[] _stackFrames;

            public CrashInfoStack(StackFrameJson[] stackFrames, IModuleService moduleService)
            {
                _stackFrames = stackFrames != null ? stackFrames.Select((frame) => new CrashInfoStackFrame(frame, moduleService)).ToArray() : Array.Empty<IStackFrame>();
            }

            public int FrameCount => _stackFrames.Length;

            public IStackFrame GetStackFrame(int index) => _stackFrames[index];
        }

        private sealed class CrashInfoStackFrame : IStackFrame
        {
            private readonly StackFrameJson _stackFrame;
            private readonly IModuleService _moduleService;

            public CrashInfoStackFrame(StackFrameJson stackFrame, IModuleService moduleService)
            {
                Debug.Assert(stackFrame != null);
                Debug.Assert(moduleService != null);
                _stackFrame = stackFrame;
                _moduleService = moduleService;
            }

            public ulong InstructionPointer => _stackFrame.InstructionPointer;

            public ulong StackPointer => _stackFrame.StackPointer;

            public ulong ModuleBase => _stackFrame.ModuleBase;

            public void GetMethodName(out string moduleName, out string methodName, out ulong displacement)
            {
                moduleName = null;
                methodName = _stackFrame.MethodName;
                displacement = _stackFrame.Offset;

                if (ModuleBase != 0)
                {
                    IModule module = _moduleService.GetModuleFromBaseAddress(_stackFrame.ModuleBase);
                    if (module != null)
                    {
                        moduleName = Path.GetFileNameWithoutExtension(module.FileName);
                        if (string.IsNullOrEmpty(methodName))
                        {
                            IModuleSymbols moduleSymbols = module.Services.GetService<IModuleSymbols>();
                            moduleSymbols?.TryGetSymbolName(_stackFrame.InstructionPointer, out methodName, out displacement);
                        }
                    }
                }
            }
        }

        public static ICrashInfoService Create(uint hresult, ReadOnlySpan<byte> triageBuffer, IModuleService moduleService)
        {
            CrashInfoService crashInfoService = null;
            try
            {
                JsonSerializerOptions options = new() { AllowTrailingCommas = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };
                CrashInfoJson crashInfo = JsonSerializer.Deserialize<CrashInfoJson>(triageBuffer, options);
                if (crashInfo != null)
                {
                    if (Version.TryParse(crashInfo.Version, out Version protocolVersion) && protocolVersion.Major >= 1)
                    {
                        crashInfoService = new(crashInfo.Thread, hresult, crashInfo, moduleService);
                    }
                    else
                    {
                        Trace.TraceError($"CrashInfoService: invalid or not supported protocol version {crashInfo.Version}");
                    }
                }
                else
                {
                    Trace.TraceError($"CrashInfoService: JsonSerializer.Deserialize failed");
                }
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException or DecoderFallbackException or ArgumentException)
            {
                Trace.TraceError($"CrashInfoService: {ex}");
            }
            return crashInfoService;
        }

        private readonly IException _exception;

        private CrashInfoService(uint threadId, uint hresult, CrashInfoJson crashInfo, IModuleService moduleService)
        {
            ThreadId = threadId;
            HResult = hresult;
            CrashReason = (CrashReason)crashInfo.Reason;
            RuntimeBaseAddress = crashInfo.RuntimeBaseAddress;
            RuntimeVersion = crashInfo.RuntimeVersion;
            RuntimeType = (RuntimeType)crashInfo.RuntimeType;
            Message = crashInfo.Message;
            _exception = crashInfo.Exception != null ? new CrashInfoException(crashInfo.Exception, moduleService) : null;
        }

        #region ICrashInfoService

        public uint ThreadId { get; }

        public uint HResult { get; }

        public CrashReason CrashReason { get; }

        public ulong RuntimeBaseAddress { get; }

        public RuntimeType RuntimeType { get; }

        public string RuntimeVersion { get; }

        public string Message { get; }

        public IException GetException(ulong address)
        {
            // Only supports getting the "current" exception or the crash exception.
            if (address != 0)
            {
                if (address != _exception.Address)
                {
                    throw new ArgumentOutOfRangeException(nameof(address));
                }
            }
            return _exception;
        }

        public IException GetThreadException(uint threadId)
        {
            // Only supports getting the "current" exception or the crash thread's exception
            if (threadId != 0)
            {
                if (threadId != ThreadId)
                {
                    throw new ArgumentOutOfRangeException(nameof(threadId));
                }
            }
            return _exception;
        }

        public IEnumerable<IException> GetNestedExceptions(uint threadId)
        {
            // Only supports getting the "current" exception or the crash thread's exception
            if (threadId != 0)
            {
                if (threadId != ThreadId)
                {
                    throw new ArgumentOutOfRangeException(nameof(threadId));
                }
            }

            List<IException> exceptions = new() {
                _exception
            };

            AddExceptions(_exception.InnerExceptions);

            void AddExceptions(IEnumerable<IException> inner)
            {
                foreach (IException exception in inner)
                {
                    exceptions.Add(exception);
                    AddExceptions(exception.InnerExceptions);
                }
            }

            return exceptions;
        }

        #endregion
    }
}
