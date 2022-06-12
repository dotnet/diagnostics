// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bc0d583f-126d-43a1-9cc4-a860ab1d537b")]
    public interface IDebugControl6 : IDebugControl5
    {
        /* IDebugControl */

        [PreserveSig]
        new int GetInterrupt();

        [PreserveSig]
        new int SetInterrupt(
            DEBUG_INTERRUPT Flags);

        [PreserveSig]
        new int GetInterruptTimeout(
            out uint Seconds);

        [PreserveSig]
        new int SetInterruptTimeout(
            uint Seconds);

        [PreserveSig]
        new int GetLogFile(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint FileSize,
            [Out][MarshalAs(UnmanagedType.Bool)] out bool Append);

        [PreserveSig]
        new int OpenLogFile(
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            [In][MarshalAs(UnmanagedType.Bool)] bool Append);

        [PreserveSig]
        new int CloseLogFile();

        [PreserveSig]
        new int GetLogMask(
            out DEBUG_OUTPUT Mask);

        [PreserveSig]
        new int SetLogMask(
            DEBUG_OUTPUT Mask);

        [PreserveSig]
        new int Input(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint InputSize);

        [PreserveSig]
        new int ReturnInput(
            [In][MarshalAs(UnmanagedType.LPStr)] string Buffer);

        [PreserveSig]
        new int Output(
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format);

        [PreserveSig]
        new int OutputVaList( /* THIS SHOULD NEVER BE CALLED FROM C# */
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format,
            IntPtr va_list_Args);

        [PreserveSig]
        new int ControlledOutput(
            DEBUG_OUTCTL OutputControl,
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format);

        [PreserveSig]
        new int ControlledOutputVaList( /* THIS SHOULD NEVER BE CALLED FROM C# */
            DEBUG_OUTCTL OutputControl,
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format,
            IntPtr va_list_Args);

        [PreserveSig]
        new int OutputPrompt(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format);

        [PreserveSig]
        new int OutputPromptVaList( /* THIS SHOULD NEVER BE CALLED FROM C# */
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format,
            IntPtr va_list_Args);

        [PreserveSig]
        new int GetPromptText(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint TextSize);

        [PreserveSig]
        new int OutputCurrentState(
            DEBUG_OUTCTL OutputControl,
            DEBUG_CURRENT Flags);

        [PreserveSig]
        new int OutputVersionInformation(
            DEBUG_OUTCTL OutputControl);

        [PreserveSig]
        new int GetNotifyEventHandle(
            out ulong Handle);

        [PreserveSig]
        new int SetNotifyEventHandle(
            ulong Handle);

        [PreserveSig]
        new int Assemble(
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPStr)] string Instr,
            out ulong EndOffset);

        [PreserveSig]
        new int Disassemble(
            ulong Offset,
            DEBUG_DISASM Flags,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint DisassemblySize,
            out ulong EndOffset);

        [PreserveSig]
        new int GetDisassembleEffectiveOffset(
            out ulong Offset);

        [PreserveSig]
        new int OutputDisassembly(
            DEBUG_OUTCTL OutputControl,
            ulong Offset,
            DEBUG_DISASM Flags,
            out ulong EndOffset);

        [PreserveSig]
        new int OutputDisassemblyLines(
            DEBUG_OUTCTL OutputControl,
            uint PreviousLines,
            uint TotalLines,
            ulong Offset,
            DEBUG_DISASM Flags,
            out uint OffsetLine,
            out ulong StartOffset,
            out ulong EndOffset,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            ulong[] LineOffsets);

        [PreserveSig]
        new int GetNearInstruction(
            ulong Offset,
            int Delta,
            out ulong NearOffset);

        [PreserveSig]
        new int GetStackTrace(
            ulong FrameOffset,
            ulong StackOffset,
            ulong InstructionOffset,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_STACK_FRAME[] Frames,
            int FrameSize,
            out uint FramesFilled);

        [PreserveSig]
        new int GetReturnOffset(
            out ulong Offset);

        [PreserveSig]
        new int OutputStackTrace(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME[] Frames,
            int FramesSize,
            DEBUG_STACK Flags);

        [PreserveSig]
        new int GetDebuggeeType(
            out DEBUG_CLASS Class,
            out DEBUG_CLASS_QUALIFIER Qualifier);

        [PreserveSig]
        new int GetActualProcessorType(
            out IMAGE_FILE_MACHINE Type);

        [PreserveSig]
        new int GetExecutingProcessorType(
            out IMAGE_FILE_MACHINE Type);

        [PreserveSig]
        new int GetNumberPossibleExecutingProcessorTypes(
            out uint Number);

        [PreserveSig]
        new int GetPossibleExecutingProcessorTypes(
            uint Start,
            uint Count,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            IMAGE_FILE_MACHINE[] Types);

        [PreserveSig]
        new int GetNumberProcessors(
            out uint Number);

        [PreserveSig]
        new int GetSystemVersion(
            out uint PlatformId,
            out uint Major,
            out uint Minor,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder ServicePackString,
            int ServicePackStringSize,
            out uint ServicePackStringUsed,
            out uint ServicePackNumber,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder BuildString,
            int BuildStringSize,
            out uint BuildStringUsed);

        [PreserveSig]
        new int GetPageSize(
            out uint Size);

        [PreserveSig]
        new int IsPointer64Bit();

        [PreserveSig]
        new int ReadBugCheckData(
            out uint Code,
            out ulong Arg1,
            out ulong Arg2,
            out ulong Arg3,
            out ulong Arg4);

        [PreserveSig]
        new int GetNumberSupportedProcessorTypes(
            out uint Number);

        [PreserveSig]
        new int GetSupportedProcessorTypes(
            uint Start,
            uint Count,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            IMAGE_FILE_MACHINE[] Types);

        [PreserveSig]
        new int GetProcessorTypeNames(
            IMAGE_FILE_MACHINE Type,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder FullNameBuffer,
            int FullNameBufferSize,
            out uint FullNameSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder AbbrevNameBuffer,
            int AbbrevNameBufferSize,
            out uint AbbrevNameSize);

        [PreserveSig]
        new int GetEffectiveProcessorType(
            out IMAGE_FILE_MACHINE Type);

        [PreserveSig]
        new int SetEffectiveProcessorType(
            IMAGE_FILE_MACHINE Type);

        [PreserveSig]
        new int GetExecutionStatus(
            out DEBUG_STATUS Status);

        [PreserveSig]
        new int SetExecutionStatus(
            DEBUG_STATUS Status);

        [PreserveSig]
        new int GetCodeLevel(
            out DEBUG_LEVEL Level);

        [PreserveSig]
        new int SetCodeLevel(
            DEBUG_LEVEL Level);

        [PreserveSig]
        new int GetEngineOptions(
            out DEBUG_ENGOPT Options);

        [PreserveSig]
        new int AddEngineOptions(
            DEBUG_ENGOPT Options);

        [PreserveSig]
        new int RemoveEngineOptions(
            DEBUG_ENGOPT Options);

        [PreserveSig]
        new int SetEngineOptions(
            DEBUG_ENGOPT Options);

        [PreserveSig]
        new int GetSystemErrorControl(
            out ERROR_LEVEL OutputLevel,
            out ERROR_LEVEL BreakLevel);

        [PreserveSig]
        new int SetSystemErrorControl(
            ERROR_LEVEL OutputLevel,
            ERROR_LEVEL BreakLevel);

        [PreserveSig]
        new int GetTextMacro(
            uint Slot,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint MacroSize);

        [PreserveSig]
        new int SetTextMacro(
            uint Slot,
            [In][MarshalAs(UnmanagedType.LPStr)] string Macro);

        [PreserveSig]
        new int GetRadix(
            out uint Radix);

        [PreserveSig]
        new int SetRadix(
            uint Radix);

        [PreserveSig]
        new int Evaluate(
            [In][MarshalAs(UnmanagedType.LPStr)] string Expression,
            DEBUG_VALUE_TYPE DesiredType,
            out DEBUG_VALUE Value,
            out uint RemainderIndex);

        [PreserveSig]
        new int CoerceValue(
            DEBUG_VALUE In,
            DEBUG_VALUE_TYPE OutType,
            out DEBUG_VALUE Out);

        [PreserveSig]
        new int CoerceValues(
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE[] In,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE_TYPE[] OutType,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_VALUE[] Out);

        [PreserveSig]
        new int Execute(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string Command,
            DEBUG_EXECUTE Flags);

        [PreserveSig]
        new int ExecuteCommandFile(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string CommandFile,
            DEBUG_EXECUTE Flags);

        [PreserveSig]
        new int GetNumberBreakpoints(
            out uint Number);

        [PreserveSig]
        new int GetBreakpointByIndex(
            uint Index,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugBreakpoint bp);

        [PreserveSig]
        new int GetBreakpointById(
            uint Id,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugBreakpoint bp);

        [PreserveSig]
        new int GetBreakpointParameters(
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Ids,
            uint Start,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_BREAKPOINT_PARAMETERS[] Params);

        [PreserveSig]
        new int AddBreakpoint(
            DEBUG_BREAKPOINT_TYPE Type,
            uint DesiredId,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugBreakpoint Bp);

        [PreserveSig]
        new int RemoveBreakpoint(
            [In][MarshalAs(UnmanagedType.Interface)]
            IDebugBreakpoint Bp);

        [PreserveSig]
        new int AddExtension(
            [In][MarshalAs(UnmanagedType.LPStr)] string Path,
            uint Flags,
            out ulong Handle);

        [PreserveSig]
        new int RemoveExtension(
            ulong Handle);

        [PreserveSig]
        new int GetExtensionByPath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Path,
            out ulong Handle);

        [PreserveSig]
        new int CallExtension(
            ulong Handle,
            [In][MarshalAs(UnmanagedType.LPStr)] string Function,
            [In][MarshalAs(UnmanagedType.LPStr)] string Arguments);

        [PreserveSig]
        new int GetExtensionFunction(
            ulong Handle,
            [In][MarshalAs(UnmanagedType.LPStr)] string FuncName,
            out IntPtr Function);

        [PreserveSig]
        new int GetWindbgExtensionApis32(
            ref WINDBG_EXTENSION_APIS Api);

        /* Must be In and Out as the nSize member has to be initialized */

        [PreserveSig]
        new int GetWindbgExtensionApis64(
            ref WINDBG_EXTENSION_APIS Api);

        /* Must be In and Out as the nSize member has to be initialized */

        [PreserveSig]
        new int GetNumberEventFilters(
            out uint SpecificEvents,
            out uint SpecificExceptions,
            out uint ArbitraryExceptions);

        [PreserveSig]
        new int GetEventFilterText(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint TextSize);

        [PreserveSig]
        new int GetEventFilterCommand(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint CommandSize);

        [PreserveSig]
        new int SetEventFilterCommand(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Command);

        [PreserveSig]
        new int GetSpecificFilterParameters(
            uint Start,
            uint Count,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_SPECIFIC_FILTER_PARAMETERS[] Params);

        [PreserveSig]
        new int SetSpecificFilterParameters(
            uint Start,
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_SPECIFIC_FILTER_PARAMETERS[] Params);

        [PreserveSig]
        new int GetSpecificEventFilterArgument(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint ArgumentSize);

        [PreserveSig]
        new int SetSpecificEventFilterArgument(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Argument);

        [PreserveSig]
        new int GetExceptionFilterParameters(
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Codes,
            uint Start,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_EXCEPTION_FILTER_PARAMETERS[] Params);

        [PreserveSig]
        new int SetExceptionFilterParameters(
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_EXCEPTION_FILTER_PARAMETERS[] Params);

        [PreserveSig]
        new int GetExceptionFilterSecondCommand(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint CommandSize);

        [PreserveSig]
        new int SetExceptionFilterSecondCommand(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Command);

        [PreserveSig]
        new int WaitForEvent(
            DEBUG_WAIT Flags,
            uint Timeout);

        [PreserveSig]
        new int GetLastEventInformation(
            out DEBUG_EVENT Type,
            out uint ProcessId,
            out uint ThreadId,
            IntPtr ExtraInformation,
            uint ExtraInformationSize,
            out uint ExtraInformationUsed,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Description,
            int DescriptionSize,
            out uint DescriptionUsed);

        /* IDebugControl2 */

        [PreserveSig]
        new int GetCurrentTimeDate(
            out uint TimeDate);

        [PreserveSig]
        new int GetCurrentSystemUpTime(
            out uint UpTime);

        [PreserveSig]
        new int GetDumpFormatFlags(
            out DEBUG_FORMAT FormatFlags);

        [PreserveSig]
        new int GetNumberTextReplacements(
            out uint NumRepl);

        [PreserveSig]
        new int GetTextReplacement(
            [In][MarshalAs(UnmanagedType.LPStr)] string SrcText,
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder SrcBuffer,
            int SrcBufferSize,
            out uint SrcSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder DstBuffer,
            int DstBufferSize,
            out uint DstSize);

        [PreserveSig]
        new int SetTextReplacement(
            [In][MarshalAs(UnmanagedType.LPStr)] string SrcText,
            [In][MarshalAs(UnmanagedType.LPStr)] string DstText);

        [PreserveSig]
        new int RemoveTextReplacements();

        [PreserveSig]
        new int OutputTextReplacements(
            DEBUG_OUTCTL OutputControl,
            DEBUG_OUT_TEXT_REPL Flags);

        /* IDebugControl3 */

        [PreserveSig]
        new int GetAssemblyOptions(
            out DEBUG_ASMOPT Options);

        [PreserveSig]
        new int AddAssemblyOptions(
            DEBUG_ASMOPT Options);

        [PreserveSig]
        new int RemoveAssemblyOptions(
            DEBUG_ASMOPT Options);

        [PreserveSig]
        new int SetAssemblyOptions(
            DEBUG_ASMOPT Options);

        [PreserveSig]
        new int GetExpressionSyntax(
            out DEBUG_EXPR Flags);

        [PreserveSig]
        new int SetExpressionSyntax(
            DEBUG_EXPR Flags);

        [PreserveSig]
        new int SetExpressionSyntaxByName(
            [In][MarshalAs(UnmanagedType.LPStr)] string AbbrevName);

        [PreserveSig]
        new int GetNumberExpressionSyntaxes(
            out uint Number);

        [PreserveSig]
        new int GetExpressionSyntaxNames(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder FullNameBuffer,
            int FullNameBufferSize,
            out uint FullNameSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder AbbrevNameBuffer,
            int AbbrevNameBufferSize,
            out uint AbbrevNameSize);

        [PreserveSig]
        new int GetNumberEvents(
            out uint Events);

        [PreserveSig]
        new int GetEventIndexDescription(
            uint Index,
            DEBUG_EINDEX Which,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint DescSize);

        [PreserveSig]
        new int GetCurrentEventIndex(
            out uint Index);

        [PreserveSig]
        new int SetNextEventIndex(
            DEBUG_EINDEX Relation,
            uint Value,
            out uint NextIndex);

        /* IDebugControl4 */

        [PreserveSig]
        new int GetLogFileWide(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint FileSize,
            [Out][MarshalAs(UnmanagedType.Bool)] out bool Append);

        [PreserveSig]
        new int OpenLogFileWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string File,
            [In][MarshalAs(UnmanagedType.Bool)] bool Append);

        [PreserveSig]
        new int InputWide(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint InputSize);

        [PreserveSig]
        new int ReturnInputWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Buffer);

        [PreserveSig]
        new int OutputWide(
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Format);

        [PreserveSig]
        new int OutputVaListWide( /* THIS SHOULD NEVER BE CALLED FROM C# */
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Format,
            IntPtr va_list_Args);

        [PreserveSig]
        new int ControlledOutputWide(
            DEBUG_OUTCTL OutputControl,
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Format);

        [PreserveSig]
        new int ControlledOutputVaListWide( /* THIS SHOULD NEVER BE CALLED FROM C# */
            DEBUG_OUTCTL OutputControl,
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Format,
            IntPtr va_list_Args);

        [PreserveSig]
        new int OutputPromptWide(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Format);

        [PreserveSig]
        new int OutputPromptVaListWide( /* THIS SHOULD NEVER BE CALLED FROM C# */
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Format,
            IntPtr va_list_Args);

        [PreserveSig]
        new int GetPromptTextWide(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint TextSize);

        [PreserveSig]
        new int AssembleWide(
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Instr,
            out ulong EndOffset);

        [PreserveSig]
        new int DisassembleWide(
            ulong Offset,
            DEBUG_DISASM Flags,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint DisassemblySize,
            out ulong EndOffset);

        [PreserveSig]
        new int GetProcessorTypeNamesWide(
            IMAGE_FILE_MACHINE Type,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder FullNameBuffer,
            int FullNameBufferSize,
            out uint FullNameSize,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder AbbrevNameBuffer,
            int AbbrevNameBufferSize,
            out uint AbbrevNameSize);

        [PreserveSig]
        new int GetTextMacroWide(
            uint Slot,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint MacroSize);

        [PreserveSig]
        new int SetTextMacroWide(
            uint Slot,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Macro);

        [PreserveSig]
        new int EvaluateWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Expression,
            DEBUG_VALUE_TYPE DesiredType,
            out DEBUG_VALUE Value,
            out uint RemainderIndex);

        [PreserveSig]
        new int ExecuteWide(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Command,
            DEBUG_EXECUTE Flags);

        [PreserveSig]
        new int ExecuteCommandFileWide(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPWStr)] string CommandFile,
            DEBUG_EXECUTE Flags);

        [PreserveSig]
        new int GetBreakpointByIndex2(
            uint Index,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugBreakpoint2 bp);

        [PreserveSig]
        new int GetBreakpointById2(
            uint Id,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugBreakpoint2 bp);

        [PreserveSig]
        new int AddBreakpoint2(
            DEBUG_BREAKPOINT_TYPE Type,
            uint DesiredId,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugBreakpoint2 Bp);

        [PreserveSig]
        new int RemoveBreakpoint2(
            [In][MarshalAs(UnmanagedType.Interface)]
            IDebugBreakpoint2 Bp);

        [PreserveSig]
        new int AddExtensionWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Path,
            uint Flags,
            out ulong Handle);

        [PreserveSig]
        new int GetExtensionByPathWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Path,
            out ulong Handle);

        [PreserveSig]
        new int CallExtensionWide(
            ulong Handle,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Function,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Arguments);

        [PreserveSig]
        new int GetExtensionFunctionWide(
            ulong Handle,
            [In][MarshalAs(UnmanagedType.LPWStr)] string FuncName,
            out IntPtr Function);

        [PreserveSig]
        new int GetEventFilterTextWide(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint TextSize);

        [PreserveSig]
        new int GetEventFilterCommandWide(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint CommandSize);

        [PreserveSig]
        new int SetEventFilterCommandWide(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Command);

        [PreserveSig]
        new int GetSpecificEventFilterArgumentWide(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint ArgumentSize);

        [PreserveSig]
        new int SetSpecificEventFilterArgumentWide(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Argument);

        [PreserveSig]
        new int GetExceptionFilterSecondCommandWide(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint CommandSize);

        [PreserveSig]
        new int SetExceptionFilterSecondCommandWide(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Command);

        [PreserveSig]
        new int GetLastEventInformationWide(
            out DEBUG_EVENT Type,
            out uint ProcessId,
            out uint ThreadId,
            IntPtr ExtraInformation,
            int ExtraInformationSize,
            out uint ExtraInformationUsed,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Description,
            int DescriptionSize,
            out uint DescriptionUsed);

        [PreserveSig]
        new int GetTextReplacementWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string SrcText,
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder SrcBuffer,
            int SrcBufferSize,
            out uint SrcSize,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder DstBuffer,
            int DstBufferSize,
            out uint DstSize);

        [PreserveSig]
        new int SetTextReplacementWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string SrcText,
            [In][MarshalAs(UnmanagedType.LPWStr)] string DstText);

        [PreserveSig]
        new int SetExpressionSyntaxByNameWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string AbbrevName);

        [PreserveSig]
        new int GetExpressionSyntaxNamesWide(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder FullNameBuffer,
            int FullNameBufferSize,
            out uint FullNameSize,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder AbbrevNameBuffer,
            int AbbrevNameBufferSize,
            out uint AbbrevNameSize);

        [PreserveSig]
        new int GetEventIndexDescriptionWide(
            uint Index,
            DEBUG_EINDEX Which,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint DescSize);

        [PreserveSig]
        new int GetLogFile2(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint FileSize,
            out DEBUG_LOG Flags);

        [PreserveSig]
        new int OpenLogFile2(
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            out DEBUG_LOG Flags);

        [PreserveSig]
        new int GetLogFile2Wide(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint FileSize,
            out DEBUG_LOG Flags);

        [PreserveSig]
        new int OpenLogFile2Wide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string File,
            out DEBUG_LOG Flags);

        [PreserveSig]
        new int GetSystemVersionValues(
            out uint PlatformId,
            out uint Win32Major,
            out uint Win32Minor,
            out uint KdMajor,
            out uint KdMinor);

        [PreserveSig]
        new int GetSystemVersionString(
            DEBUG_SYSVERSTR Which,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint StringSize);

        [PreserveSig]
        new int GetSystemVersionStringWide(
            DEBUG_SYSVERSTR Which,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint StringSize);

        [PreserveSig]
        new int GetContextStackTrace(
            IntPtr StartContext,
            uint StartContextSize,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_STACK_FRAME[] Frames,
            int FrameSize,
            IntPtr FrameContexts,
            uint FrameContextsSize,
            uint FrameContextsEntrySize,
            out uint FramesFilled);

        [PreserveSig]
        new int OutputContextStackTrace(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME[] Frames,
            int FramesSize,
            IntPtr FrameContexts,
            uint FrameContextsSize,
            uint FrameContextsEntrySize,
            DEBUG_STACK Flags);

        [PreserveSig]
        new int GetStoredEventInformation(
            out DEBUG_EVENT Type,
            out uint ProcessId,
            out uint ThreadId,
            IntPtr Context,
            uint ContextSize,
            out uint ContextUsed,
            IntPtr ExtraInformation,
            uint ExtraInformationSize,
            out uint ExtraInformationUsed);

        [PreserveSig]
        new int GetManagedStatus(
            out DEBUG_MANAGED Flags,
            DEBUG_MANSTR WhichString,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder String,
            int StringSize,
            out uint StringNeeded);

        [PreserveSig]
        new int GetManagedStatusWide(
            out DEBUG_MANAGED Flags,
            DEBUG_MANSTR WhichString,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder String,
            int StringSize,
            out uint StringNeeded);

        [PreserveSig]
        new int ResetManagedStatus(
            DEBUG_MANRESET Flags);

        /* IDebugControl5 */

        [PreserveSig]
        new int GetStackTraceEx(
            ulong FrameOffset,
            ulong StackOffset,
            ulong InstructionOffset,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_STACK_FRAME_EX[] Frames,
            int FramesSize,
            out uint FramesFilled);

        [PreserveSig]
        new int OutputStackTraceEx(
            uint OutputControl,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME_EX[] Frames,
            int FramesSize,
            DEBUG_STACK Flags);

        [PreserveSig]
        new int GetContextStackTraceEx(
            IntPtr StartContext,
            uint StartContextSize,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_STACK_FRAME_EX[] Frames,
            int FramesSize,
            IntPtr FrameContexts,
            uint FrameContextsSize,
            uint FrameContextsEntrySize,
            out uint FramesFilled);

        [PreserveSig]
        new int OutputContextStackTraceEx(
            uint OutputControl,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME_EX[] Frames,
            int FramesSize,
            IntPtr FrameContexts,
            uint FrameContextsSize,
            uint FrameContextsEntrySize,
            DEBUG_STACK Flags);

        [PreserveSig]
        new int GetBreakpointByGuid(
            [In][MarshalAs(UnmanagedType.LPStruct)]
            Guid Guid,
            out IDebugBreakpoint3 Bp);

        /* IDebugControl6 */

        [PreserveSig]
        int GetExecutionStatusEx(out DEBUG_STATUS Status);

        [PreserveSig]
        int GetSynchronizationStatus(
            out uint SendsAttempted,
            out uint SecondsSinceLastResponse);
    }
}