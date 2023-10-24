// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public sealed class CrashInfoJson
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
            public CrashInfoException Exception { get; set; }
        }

        public sealed class CrashInfoException : IManagedException
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
            public CrashInfoStackFrame[] Stack { get; set; }

            IEnumerable<IStackFrame> IManagedException.Stack => Stack;

            [JsonPropertyName("inner")]
            public CrashInfoException[] InnerExceptions { get; set; }

            IEnumerable<IManagedException> IManagedException.InnerExceptions => InnerExceptions;
        }

        public sealed class CrashInfoStackFrame : IStackFrame
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

        public sealed class HexUInt64Converter : JsonConverter<ulong>
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

        public sealed class HexUInt32Converter : JsonConverter<uint>
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

        public static ICrashInfoService Create(uint hresult, ReadOnlySpan<byte> triageBuffer)
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
                        crashInfoService = new(crashInfo.Thread, hresult, crashInfo);
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
            catch (Exception ex) when (ex is JsonException or NotSupportedException or DecoderFallbackException or ArgumentException)
            {
                Trace.TraceError($"CrashInfoService: {ex}");
            }
            return crashInfoService;
        }

        private CrashInfoService(uint threadId, uint hresult, CrashInfoJson crashInfo)
        {
            ThreadId = threadId;
            HResult = hresult;
            CrashReason = (CrashReason)crashInfo.Reason;
            RuntimeBaseAddress = crashInfo.RuntimeBaseAddress;
            RuntimeVersion = crashInfo.RuntimeVersion;
            RuntimeType = (RuntimeType)crashInfo.RuntimeType;
            Message = crashInfo.Message;
            Exception = crashInfo.Exception;
        }

        #region ICrashInfoService

        public uint ThreadId { get; }

        public uint HResult { get; }

        public CrashReason CrashReason { get; }

        public ulong RuntimeBaseAddress { get; }

        public RuntimeType RuntimeType { get; }

        public string RuntimeVersion { get; }

        public string Message { get; }

        public IManagedException Exception { get; }

        #endregion
    }
}
