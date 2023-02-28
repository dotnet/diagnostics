// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Hosting.DbgEng
{
    internal unsafe class DebugControl
    {
        internal DebugControl(DebugClient client, SOSHost soshost)
        {
            VTableBuilder builder = client.AddInterface(typeof(IDebugControl).GUID, validate: true);
            AddDebugControl(builder, soshost);
            builder.Complete();

            builder = client.AddInterface(typeof(IDebugControl2).GUID, validate: true);
            AddDebugControl2(builder, soshost);
            builder.Complete();
        }

        private static void AddDebugControl(VTableBuilder builder, SOSHost soshost)
        {
            builder.AddMethod(new GetInterruptDelegate(soshost.GetInterrupt));
            builder.AddMethod(new SetInterruptDelegate((self, flags) => HResult.S_OK));
            builder.AddMethod(new GetInterruptTimeoutDelegate((self, seconds) => DebugClient.NotImplemented));
            builder.AddMethod(new SetInterruptTimeoutDelegate((self, seconds) => DebugClient.NotImplemented));
            builder.AddMethod(new GetLogFileDelegate((self, buffer, bufferSize, fileSize, append) => DebugClient.NotImplemented));
            builder.AddMethod(new OpenLogFileDelegate((self, file, append) => DebugClient.NotImplemented));
            builder.AddMethod(new CloseLogFileDelegate((self) => DebugClient.NotImplemented));
            builder.AddMethod(new GetLogMaskDelegate((self, mask) => DebugClient.NotImplemented));
            builder.AddMethod(new SetLogMaskDelegate((self, mask) => DebugClient.NotImplemented));
            builder.AddMethod(new InputDelegate((self, buffer, bufferSize, inputSize) => DebugClient.NotImplemented));
            builder.AddMethod(new ReturnInputDelegate((self, buffer) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputDelegate((self, mask, format) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputVaListDelegate(soshost.OutputVaList));
            builder.AddMethod(new ControlledOutputDelegate((self, outputControl, mask, format) => DebugClient.NotImplemented));
            builder.AddMethod(new ControlledOutputVaListDelegate((self, outputControl, mask, format, valist) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputPromptDelegate((self, outputControl, format) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputPromptVaListDelegate((self, outputControl, format, valist) => DebugClient.NotImplemented));
            builder.AddMethod(new GetPromptTextDelegate((self, buffer, bufferSize, textSize) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputCurrentStateDelegate((self, outputControl, flags) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputVersionInformationDelegate((self, outputControl) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNotifyEventHandleDelegate((self, handle) => DebugClient.NotImplemented));
            builder.AddMethod(new SetNotifyEventHandleDelegate((self, handle) => DebugClient.NotImplemented));
            builder.AddMethod(new AssembleDelegate((self, offset, instr, endoffset) => DebugClient.NotImplemented));
            builder.AddMethod(new DisassembleDelegate(SOSHost.Disassemble));
            builder.AddMethod(new GetDisassembleEffectiveOffsetDelegate((self, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputDisassemblyDelegate((self, outputControl, offset, flags, endOffset) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputDisassemblyLinesDelegate((self, outputControl, previousLines, totalLines, offset, flags, offsetLine, startOffset, EndOffset, lineOffsets) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNearInstructionDelegate((self, offset, delta, nearOffset) => DebugClient.NotImplemented));
            builder.AddMethod(new GetStackTraceDelegate((self, frameOffset, stackOffset, instructionOffset, frames, frameSize, framesFilled) => DebugClient.NotImplemented));
            builder.AddMethod(new GetReturnOffsetDelegate((self, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputStackTraceDelegate((self, outputControl, frames, frameSize, flags) => DebugClient.NotImplemented));
            builder.AddMethod(new GetDebuggeeTypeDelegate(soshost.GetDebuggeeType));
            builder.AddMethod(new GetActualProcessorTypeDelegate((self, type) => DebugClient.NotImplemented));
            builder.AddMethod(new GetExecutingProcessorTypeDelegate(soshost.GetExecutingProcessorType));
            builder.AddMethod(new GetNumberPossibleExecutingProcessorTypesDelegate((self, number) => DebugClient.NotImplemented));
            builder.AddMethod(new GetPossibleExecutingProcessorTypesDelegate((self, start, count, types) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNumberProcessorsDelegate((self, number) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSystemVersionDelegate((self, platformId, major, minor, servicePack, servicePackSize, servicePackUsed, servicePackNumber, buildString, buildStringSize, buildStringUse) => DebugClient.NotImplemented));
            builder.AddMethod(new GetPageSizeDelegate(soshost.GetPageSize));
            builder.AddMethod(new IsPointer64BitDelegate((self) => DebugClient.NotImplemented));
            builder.AddMethod(new ReadBugCheckDataDelegate((self, code, arg1, arg2, arg3, arg4) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNumberSupportedProcessorTypesDelegate((self, number) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSupportedProcessorTypesDelegate((self, start, count, types) => DebugClient.NotImplemented));
            builder.AddMethod(new GetProcessorTypeNamesDelegate((self, type, fullNameBuffer, fullNameBufferSize, fullNameSize, abbrevNameBuffer, abbrevNameBufferSize, abbrevNameSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetEffectiveProcessorTypeDelegate((self, type) => DebugClient.NotImplemented));
            builder.AddMethod(new SetEffectiveProcessorTypeDelegate((self, type) => DebugClient.NotImplemented));
            builder.AddMethod(new GetExecutionStatusDelegate((self, status) => DebugClient.NotImplemented));
            builder.AddMethod(new SetExecutionStatusDelegate((self, status) => DebugClient.NotImplemented));
            builder.AddMethod(new GetCodeLevelDelegate((self, level) => DebugClient.NotImplemented));
            builder.AddMethod(new SetCodeLevelDelegate((self, level) => DebugClient.NotImplemented));
            builder.AddMethod(new GetEngineOptionsDelegate((self, options) => HResult.E_NOTIMPL));
            builder.AddMethod(new AddEngineOptionsDelegate((self, options) => HResult.E_NOTIMPL));
            builder.AddMethod(new RemoveEngineOptionsDelegate((self, options) => DebugClient.NotImplemented));
            builder.AddMethod(new SetEngineOptionsDelegate((self, options) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSystemErrorControlDelegate((self, outputLevel, breakLevel) => DebugClient.NotImplemented));
            builder.AddMethod(new SetSystemErrorControlDelegate((self, outputLevel, breakLevel) => DebugClient.NotImplemented));
            builder.AddMethod(new GetTextMacroDelegate((self, slot, buffer, bufferSize, macroSize) => DebugClient.NotImplemented));
            builder.AddMethod(new SetTextMacroDelegate((self, slot, macro) => DebugClient.NotImplemented));
            builder.AddMethod(new GetRadixDelegate((self, radix) => DebugClient.NotImplemented));
            builder.AddMethod(new SetRadixDelegate((self, radix) => DebugClient.NotImplemented));
            builder.AddMethod(new EvaluateDelegate((self, expression, desiredType, value, remainderIndex) => DebugClient.NotImplemented));
            builder.AddMethod(new CoerceValueDelegate((self, inValue, outType, outValue) => DebugClient.NotImplemented));
            builder.AddMethod(new CoerceValuesDelegate((self, count, inValues, outTypes, outValues) => DebugClient.NotImplemented));
            builder.AddMethod(new ExecuteDelegate(SOSHost.Execute));
            builder.AddMethod(new ExecuteCommandFileDelegate((self, outputControl, commandFile, flags) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNumberBreakpointsDelegate((self, number) => DebugClient.NotImplemented));
            builder.AddMethod(new GetBreakpointByIndexDelegate((self, index, bp) => DebugClient.NotImplemented));
            builder.AddMethod(new GetBreakpointByIdDelegate((self, id, bp) => DebugClient.NotImplemented));
            builder.AddMethod(new GetBreakpointParametersDelegate((self, count, ids, start, bpParams) => DebugClient.NotImplemented));
            builder.AddMethod(new AddBreakpointDelegate((self, type, desiredid, bp) => DebugClient.NotImplemented));
            builder.AddMethod(new RemoveBreakpointDelegate((self, bp) => DebugClient.NotImplemented));
            builder.AddMethod(new AddExtensionDelegate((self, path, flags, handle) => DebugClient.NotImplemented));
            builder.AddMethod(new RemoveExtensionDelegate((self, handle) => DebugClient.NotImplemented));
            builder.AddMethod(new GetExtensionByPathDelegate((self, path, handle) => DebugClient.NotImplemented));
            builder.AddMethod(new CallExtensionDelegate((self, handle, function, arguments) => DebugClient.NotImplemented));
            builder.AddMethod(new GetExtensionFunctionDelegate((self, handle, functionName, function) => DebugClient.NotImplemented));
            builder.AddMethod(new GetWindbgExtensionApis32Delegate((self, api) => DebugClient.NotImplemented));
            builder.AddMethod(new GetWindbgExtensionApis64Delegate((self, api) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNumberEventFiltersDelegate((self, specificEvents, specificExceptions, arbitraryExceptions) => DebugClient.NotImplemented));
            builder.AddMethod(new GetEventFilterTextDelegate((self, index, buffer, bufferSize, textSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetEventFilterCommandDelegate((self, index, buffer, bufferSize, commandSize) => DebugClient.NotImplemented));
            builder.AddMethod(new SetEventFilterCommandDelegate((self, index, command) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSpecificFilterParametersDelegate((self, start, count, filterParams) => DebugClient.NotImplemented));
            builder.AddMethod(new SetSpecificFilterParametersDelegate((self, start, count, filterParams) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSpecificEventFilterArgumentDelegate((self, index, buffer, bufferSize, argumentSize) => DebugClient.NotImplemented));
            builder.AddMethod(new SetSpecificEventFilterArgumentDelegate((self, index, argument) => DebugClient.NotImplemented));
            builder.AddMethod(new GetExceptionFilterParametersDelegate((self, count, codes, start, filterParams) => DebugClient.NotImplemented));
            builder.AddMethod(new SetExceptionFilterParametersDelegate((self, count, filterParams) => DebugClient.NotImplemented));
            builder.AddMethod(new GetExceptionFilterSecondCommandDelegate((self, index, buffer, bufferSize, commandSize) => DebugClient.NotImplemented));
            builder.AddMethod(new SetExceptionFilterSecondCommandDelegate((self, index, command) => DebugClient.NotImplemented));
            builder.AddMethod(new WaitForEventDelegate((self, flags, timeout) => DebugClient.NotImplemented));
            builder.AddMethod(new GetLastEventInformationDelegate(soshost.GetLastEventInformation));
        }

        private static void AddDebugControl2(VTableBuilder builder, SOSHost soshost)
        {
            AddDebugControl(builder, soshost);
            builder.AddMethod(new GetCurrentTimeDateDelegate((self, timeDate) => DebugClient.NotImplemented));
            builder.AddMethod(new GetCurrentSystemUpTimeDelegate((self, uptime) => DebugClient.NotImplemented));
            builder.AddMethod(new GetDumpFormatFlagsDelegate(soshost.GetDumpFormatFlags));
            builder.AddMethod(new GetNumberTextReplacementsDelegate((self, numRepl) => DebugClient.NotImplemented));
            builder.AddMethod(new GetTextReplacementDelegate((self, srcText, index, srcBuffer, srcBufferSize, srcSize, dstBuffer, dstBufferSize, dstSize) => DebugClient.NotImplemented));
            builder.AddMethod(new SetTextReplacementDelegate((self, srcText, dstText) => DebugClient.NotImplemented));
            builder.AddMethod(new RemoveTextReplacementsDelegate((self) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputTextReplacementsDelegate((self, outputControl, flags) => DebugClient.NotImplemented));
        }

        #region IDebugControl Delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetInterruptDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetInterruptDelegate(
            IntPtr self,
            [In] DEBUG_INTERRUPT Flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetInterruptTimeoutDelegate(
            IntPtr self,
            [Out] int* Seconds);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetInterruptTimeoutDelegate(
            IntPtr self,
            [In] uint Seconds);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetLogFileDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* FileSize,
            [Out][MarshalAs(UnmanagedType.Bool)] bool* Append);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OpenLogFileDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            [In][MarshalAs(UnmanagedType.Bool)] bool Append);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CloseLogFileDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetLogMaskDelegate(
            IntPtr self,
            [Out] DEBUG_OUTPUT* Mask);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetLogMaskDelegate(
            IntPtr self,
            [In] DEBUG_OUTPUT Mask);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int InputDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* InputSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReturnInputDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Buffer);

        //[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int OutputDelegate(
            IntPtr self,
            [In] DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputVaListDelegate(
            IntPtr self,
            [In] DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format,
            [In] IntPtr valist);

        //[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ControlledOutputDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In] DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ControlledOutputVaListDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In] DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format,
            [In] IntPtr valist);

        //[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int OutputPromptDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputPromptVaListDelegate( /* THIS SHOULD NEVER BE CALLED FROM C# */
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format,
            [In] IntPtr valist);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetPromptTextDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* TextSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputCurrentStateDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In] DEBUG_CURRENT Flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputVersionInformationDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNotifyEventHandleDelegate(
            IntPtr self,
            [Out] ulong* Handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetNotifyEventHandleDelegate(
            IntPtr self,
            [In] ulong Handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AssembleDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In][MarshalAs(UnmanagedType.LPStr)] string Instr,
            [Out] ulong* EndOffset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int DisassembleDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] DEBUG_DISASM Flags,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] uint BufferSize,
            [Out] uint* DisassemblySize,
            [Out] ulong* EndOffset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetDisassembleEffectiveOffsetDelegate(
            IntPtr self,
            [Out] ulong* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputDisassemblyDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In] ulong Offset,
            [In] DEBUG_DISASM Flags,
            [Out] ulong* EndOffset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputDisassemblyLinesDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In] uint PreviousLines,
            [In] uint TotalLines,
            [In] ulong Offset,
            [In] DEBUG_DISASM Flags,
            [Out] uint* OffsetLine,
            [Out] ulong* StartOffset,
            [Out] ulong* EndOffset,
            [Out] ulong* LineOffsets);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNearInstructionDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] int Delta,
            [Out] ulong* NearOffset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetStackTraceDelegate(
            IntPtr self,
            [In] ulong FrameOffset,
            [In] ulong StackOffset,
            [In] ulong InstructionOffset,
            [Out] DEBUG_STACK_FRAME* Frames,
            [In] int FrameSize,
            [Out] uint* FramesFilled);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetReturnOffsetDelegate(
            IntPtr self,
            [Out] ulong* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputStackTraceDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME[] Frames,
            [In] int FramesSize,
            [In] DEBUG_STACK Flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetDebuggeeTypeDelegate(
            IntPtr self,
            [Out] DEBUG_CLASS* Class,
            [Out] DEBUG_CLASS_QUALIFIER* Qualifier);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetActualProcessorTypeDelegate(
            IntPtr self,
            [Out] IMAGE_FILE_MACHINE* Type);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetExecutingProcessorTypeDelegate(
            IntPtr self,
            [Out] IMAGE_FILE_MACHINE* Type);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNumberPossibleExecutingProcessorTypesDelegate(
            IntPtr self,
            [Out] uint* Number);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetPossibleExecutingProcessorTypesDelegate(
            IntPtr self,
            [In] uint Start,
            [In] uint Count,
            [Out] IMAGE_FILE_MACHINE* Types);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNumberProcessorsDelegate(
            IntPtr self,
            [Out] uint* Number);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSystemVersionDelegate(
            IntPtr self,
            [Out] uint* PlatformId,
            [Out] uint* Major,
            [Out] uint* Minor,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder ServicePackString,
            [In] int ServicePackStringSize,
            [Out] uint* ServicePackStringUsed,
            [Out] uint* ServicePackNumber,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder BuildString,
            [In] int BuildStringSize,
            [Out] uint* BuildStringUsed);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetPageSizeDelegate(
            IntPtr self,
            [Out] uint* Size);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int IsPointer64BitDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadBugCheckDataDelegate(
            IntPtr self,
            [Out] uint* Code,
            [Out] ulong* Arg1,
            [Out] ulong* Arg2,
            [Out] ulong* Arg3,
            [Out] ulong* Arg4);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNumberSupportedProcessorTypesDelegate(
            IntPtr self,
            [Out] uint* Number);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSupportedProcessorTypesDelegate(
            IntPtr self,
            [In] uint Start,
            [In] uint Count,
            [Out] IMAGE_FILE_MACHINE* Types);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetProcessorTypeNamesDelegate(
            IntPtr self,
            [In] IMAGE_FILE_MACHINE Type,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder FullNameBuffer,
            [In] int FullNameBufferSize,
            [Out] uint* FullNameSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder AbbrevNameBuffer,
            [In] int AbbrevNameBufferSize,
            [Out] uint* AbbrevNameSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetEffectiveProcessorTypeDelegate(
            IntPtr self,
            [Out] IMAGE_FILE_MACHINE* Type);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetEffectiveProcessorTypeDelegate(
            IntPtr self,
            [In] IMAGE_FILE_MACHINE Type);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetExecutionStatusDelegate(
            IntPtr self,
            [Out] DEBUG_STATUS* Status);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetExecutionStatusDelegate(
            IntPtr self,
            [In] DEBUG_STATUS Status);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCodeLevelDelegate(
            IntPtr self,
            [Out] DEBUG_LEVEL* Level);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetCodeLevelDelegate(
            IntPtr self,
            [In] DEBUG_LEVEL Level);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetEngineOptionsDelegate(
            IntPtr self,
            [Out] DEBUG_ENGOPT* Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AddEngineOptionsDelegate(
            IntPtr self,
            [In] DEBUG_ENGOPT Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RemoveEngineOptionsDelegate(
            IntPtr self,
            [In] DEBUG_ENGOPT Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetEngineOptionsDelegate(
            IntPtr self,
            [In] DEBUG_ENGOPT Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSystemErrorControlDelegate(
            IntPtr self,
            [Out] ERROR_LEVEL* OutputLevel,
            [Out] ERROR_LEVEL* BreakLevel);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetSystemErrorControlDelegate(
            IntPtr self,
            [In] ERROR_LEVEL OutputLevel,
            [In] ERROR_LEVEL BreakLevel);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetTextMacroDelegate(
            IntPtr self,
            [In] uint Slot,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* MacroSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetTextMacroDelegate(
            IntPtr self,
            [In] uint Slot,
            [In][MarshalAs(UnmanagedType.LPStr)] string Macro);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetRadixDelegate(
            IntPtr self,
            [Out] uint* Radix);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetRadixDelegate(
            IntPtr self,
            [In] uint Radix);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int EvaluateDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Expression,
            [In] DEBUG_VALUE_TYPE DesiredType,
            [Out] DEBUG_VALUE* Value,
            [Out] uint* RemainderIndex);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CoerceValueDelegate(
            IntPtr self,
            [In] DEBUG_VALUE In,
            [In] DEBUG_VALUE_TYPE OutType,
            [Out] DEBUG_VALUE* Out);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CoerceValuesDelegate(
            IntPtr self,
            [In] uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE[] In,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE_TYPE[] OutType,
            [Out] DEBUG_VALUE* Out);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ExecuteDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string Command,
            [In] DEBUG_EXECUTE Flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ExecuteCommandFileDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string CommandFile,
            [In] DEBUG_EXECUTE Flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNumberBreakpointsDelegate(
            IntPtr self,
            [Out] uint* Number);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetBreakpointByIndexDelegate(
            IntPtr self,
            [In] uint Index,
            [Out][MarshalAs(UnmanagedType.Interface)] IntPtr bp);     // out IDebugBreakpoint

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetBreakpointByIdDelegate(
            IntPtr self,
            [In] uint Id,
            [Out][MarshalAs(UnmanagedType.Interface)] IntPtr bp);     // out IDebugBreakpoint

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetBreakpointParametersDelegate(
            IntPtr self,
            [In] uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Ids,
            [In] uint Start,
            [Out] DEBUG_BREAKPOINT_PARAMETERS* Params);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AddBreakpointDelegate(
            IntPtr self,
            [In] DEBUG_BREAKPOINT_TYPE Type,
            [In] uint DesiredId,
            [Out][MarshalAs(UnmanagedType.Interface)] IntPtr bp);     // out IDebugBreakpoint

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RemoveBreakpointDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.Interface)] IDebugBreakpoint Bp);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AddExtensionDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Path,
            [In] uint Flags,
            [Out] ulong* Handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RemoveExtensionDelegate(
            IntPtr self,
            [In] ulong Handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetExtensionByPathDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Path,
            [Out] ulong* Handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CallExtensionDelegate(
            IntPtr self,
            [In] ulong Handle,
            [In][MarshalAs(UnmanagedType.LPStr)] string Function,
            [In][MarshalAs(UnmanagedType.LPStr)] string Arguments);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetExtensionFunctionDelegate(
            IntPtr self,
            [In] ulong Handle,
            [In][MarshalAs(UnmanagedType.LPStr)] string FuncName,
            [Out] IntPtr* Function);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetWindbgExtensionApis32Delegate(
            IntPtr self,
            [In][Out] WINDBG_EXTENSION_APIS* Api);

        /* Must be In and Out as the nSize member has to be initialized */

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetWindbgExtensionApis64Delegate(
            IntPtr self,
            [In][Out] WINDBG_EXTENSION_APIS* Api);

        /* Must be In and Out as the nSize member has to be initialized */

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNumberEventFiltersDelegate(
            IntPtr self,
            [Out] uint* SpecificEvents,
            [Out] uint* SpecificExceptions,
            [Out] uint* ArbitraryExceptions);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetEventFilterTextDelegate(
            IntPtr self,
            [In] uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* TextSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetEventFilterCommandDelegate(
            IntPtr self,
            [In] uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* CommandSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetEventFilterCommandDelegate(
            IntPtr self,
            [In] uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Command);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSpecificFilterParametersDelegate(
            IntPtr self,
            [In] uint Start,
            [In] uint Count,
            [Out] DEBUG_SPECIFIC_FILTER_PARAMETERS* Params);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetSpecificFilterParametersDelegate(
            IntPtr self,
            [In] uint Start,
            [In] uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_SPECIFIC_FILTER_PARAMETERS[] Params);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSpecificEventFilterArgumentDelegate(
            IntPtr self,
            [In] uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* ArgumentSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetSpecificEventFilterArgumentDelegate(
            IntPtr self,
            [In] uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Argument);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetExceptionFilterParametersDelegate(
            IntPtr self,
            [In] uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Codes,
            [In] uint Start,
            [Out] DEBUG_EXCEPTION_FILTER_PARAMETERS* Params);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetExceptionFilterParametersDelegate(
            IntPtr self,
            [In] uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_EXCEPTION_FILTER_PARAMETERS[] Params);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetExceptionFilterSecondCommandDelegate(
            IntPtr self,
            [In] uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* CommandSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetExceptionFilterSecondCommandDelegate(
            IntPtr self,
            [In] uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Command);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WaitForEventDelegate(
            IntPtr self,
            [In] DEBUG_WAIT Flags,
            [In] uint Timeout);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetLastEventInformationDelegate(
            IntPtr self,
            [Out] DEBUG_EVENT* Type,
            [Out] uint* ProcessId,
            [Out] uint* ThreadId,
            [In] IntPtr ExtraInformation,
            [In] uint ExtraInformationSize,
            [Out] uint* ExtraInformationUsed,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Description,
            [In] uint DescriptionSize,
            [Out] uint* DescriptionUsed);

        #endregion

        #region IDebugControl2 Delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentTimeDateDelegate(
            IntPtr self,
            [Out] uint* TimeDate);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentSystemUpTimeDelegate(
            IntPtr self,
            [Out] uint* UpTime);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetDumpFormatFlagsDelegate(
            IntPtr self,
            [Out] DEBUG_FORMAT* FormatFlags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNumberTextReplacementsDelegate(
            IntPtr self,
            [Out] uint* NumRepl);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetTextReplacementDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string SrcText,
            [In] uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder SrcBuffer,
            [In] int SrcBufferSize,
            [Out] uint* SrcSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder DstBuffer,
            [In] int DstBufferSize,
            [Out] uint* DstSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetTextReplacementDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string SrcText,
            [In][MarshalAs(UnmanagedType.LPStr)] string DstText);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RemoveTextReplacementsDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputTextReplacementsDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In] DEBUG_OUT_TEXT_REPL Flags);

        #endregion
    }
}
