﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("7df74a86-b03f-407f-90ab-a20dadcead08")]
    public interface IDebugControl3 : IDebugControl2
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

        /* IDebugControl3 */

        [PreserveSig]
        int GetAssemblyOptions(
            out DEBUG_ASMOPT Options);

        [PreserveSig]
        int AddAssemblyOptions(
            DEBUG_ASMOPT Options);

        [PreserveSig]
        int RemoveAssemblyOptions(
            DEBUG_ASMOPT Options);

        [PreserveSig]
        int SetAssemblyOptions(
            DEBUG_ASMOPT Options);

        [PreserveSig]
        int GetExpressionSyntax(
            out DEBUG_EXPR Flags);

        [PreserveSig]
        int SetExpressionSyntax(
            DEBUG_EXPR Flags);

        [PreserveSig]
        int SetExpressionSyntaxByName(
            [In][MarshalAs(UnmanagedType.LPStr)] string AbbrevName);

        [PreserveSig]
        int GetNumberExpressionSyntaxes(
            out uint Number);

        [PreserveSig]
        int GetExpressionSyntaxNames(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder FullNameBuffer,
            int FullNameBufferSize,
            out uint FullNameSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder AbbrevNameBuffer,
            int AbbrevNameBufferSize,
            out uint AbbrevNameSize);

        [PreserveSig]
        int GetNumberEvents(
            out uint Events);

        [PreserveSig]
        int GetEventIndexDescription(
            uint Index,
            DEBUG_EINDEX Which,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint DescSize);

        [PreserveSig]
        int GetCurrentEventIndex(
            out uint Index);

        [PreserveSig]
        int SetNextEventIndex(
            DEBUG_EINDEX Relation,
            uint Value,
            out uint NextIndex);
    }
}