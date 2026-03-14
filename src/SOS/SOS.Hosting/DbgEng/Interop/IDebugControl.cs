// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("5182e668-105e-416e-ad92-24ef800424ba")]
    public interface IDebugControl
    {
        /* IDebugControl */

        [PreserveSig]
        int GetInterrupt();

        [PreserveSig]
        int SetInterrupt(
            DEBUG_INTERRUPT Flags);

        [PreserveSig]
        int GetInterruptTimeout(
            out uint Seconds);

        [PreserveSig]
        int SetInterruptTimeout(
            uint Seconds);

        [PreserveSig]
        int GetLogFile(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint FileSize,
            [Out][MarshalAs(UnmanagedType.Bool)] out bool Append);

        [PreserveSig]
        int OpenLogFile(
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            [In][MarshalAs(UnmanagedType.Bool)] bool Append);

        [PreserveSig]
        int CloseLogFile();

        [PreserveSig]
        int GetLogMask(
            out DEBUG_OUTPUT Mask);

        [PreserveSig]
        int SetLogMask(
            DEBUG_OUTPUT Mask);

        [PreserveSig]
        int Input(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint InputSize);

        [PreserveSig]
        int ReturnInput(
            [In][MarshalAs(UnmanagedType.LPStr)] string Buffer);

        [PreserveSig]
        int Output(
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format);

        [PreserveSig]
        int OutputVaList( /* THIS SHOULD NEVER BE CALLED FROM C# */
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format,
            IntPtr va_list_Args);

        [PreserveSig]
        int ControlledOutput(
            DEBUG_OUTCTL OutputControl,
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format);

        [PreserveSig]
        int ControlledOutputVaList( /* THIS SHOULD NEVER BE CALLED FROM C# */
            DEBUG_OUTCTL OutputControl,
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format,
            IntPtr va_list_Args);

        [PreserveSig]
        int OutputPrompt(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format);

        [PreserveSig]
        int OutputPromptVaList( /* THIS SHOULD NEVER BE CALLED FROM C# */
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format,
            IntPtr va_list_Args);

        [PreserveSig]
        int GetPromptText(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint TextSize);

        [PreserveSig]
        int OutputCurrentState(
            DEBUG_OUTCTL OutputControl,
            DEBUG_CURRENT Flags);

        [PreserveSig]
        int OutputVersionInformation(
            DEBUG_OUTCTL OutputControl);

        [PreserveSig]
        int GetNotifyEventHandle(
            out ulong Handle);

        [PreserveSig]
        int SetNotifyEventHandle(
            ulong Handle);

        [PreserveSig]
        int Assemble(
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPStr)] string Instr,
            out ulong EndOffset);

        [PreserveSig]
        int Disassemble(
            ulong Offset,
            DEBUG_DISASM Flags,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint DisassemblySize,
            out ulong EndOffset);

        [PreserveSig]
        int GetDisassembleEffectiveOffset(
            out ulong Offset);

        [PreserveSig]
        int OutputDisassembly(
            DEBUG_OUTCTL OutputControl,
            ulong Offset,
            DEBUG_DISASM Flags,
            out ulong EndOffset);

        [PreserveSig]
        int OutputDisassemblyLines(
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
        int GetNearInstruction(
            ulong Offset,
            int Delta,
            out ulong NearOffset);

        [PreserveSig]
        int GetStackTrace(
            ulong FrameOffset,
            ulong StackOffset,
            ulong InstructionOffset,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_STACK_FRAME[] Frames,
            int FrameSize,
            out uint FramesFilled);

        [PreserveSig]
        int GetReturnOffset(
            out ulong Offset);

        [PreserveSig]
        int OutputStackTrace(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME[] Frames,
            int FramesSize,
            DEBUG_STACK Flags);

        [PreserveSig]
        int GetDebuggeeType(
            out DEBUG_CLASS Class,
            out DEBUG_CLASS_QUALIFIER Qualifier);

        [PreserveSig]
        int GetActualProcessorType(
            out IMAGE_FILE_MACHINE Type);

        [PreserveSig]
        int GetExecutingProcessorType(
            out IMAGE_FILE_MACHINE Type);

        [PreserveSig]
        int GetNumberPossibleExecutingProcessorTypes(
            out uint Number);

        [PreserveSig]
        int GetPossibleExecutingProcessorTypes(
            uint Start,
            uint Count,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            IMAGE_FILE_MACHINE[] Types);

        [PreserveSig]
        int GetNumberProcessors(
            out uint Number);

        [PreserveSig]
        int GetSystemVersion(
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
        int GetPageSize(
            out uint Size);

        [PreserveSig]
        int IsPointer64Bit();

        [PreserveSig]
        int ReadBugCheckData(
            out uint Code,
            out ulong Arg1,
            out ulong Arg2,
            out ulong Arg3,
            out ulong Arg4);

        [PreserveSig]
        int GetNumberSupportedProcessorTypes(
            out uint Number);

        [PreserveSig]
        int GetSupportedProcessorTypes(
            uint Start,
            uint Count,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            IMAGE_FILE_MACHINE[] Types);

        [PreserveSig]
        int GetProcessorTypeNames(
            IMAGE_FILE_MACHINE Type,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder FullNameBuffer,
            int FullNameBufferSize,
            out uint FullNameSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder AbbrevNameBuffer,
            int AbbrevNameBufferSize,
            out uint AbbrevNameSize);

        [PreserveSig]
        int GetEffectiveProcessorType(
            out IMAGE_FILE_MACHINE Type);

        [PreserveSig]
        int SetEffectiveProcessorType(
            IMAGE_FILE_MACHINE Type);

        [PreserveSig]
        int GetExecutionStatus(
            out DEBUG_STATUS Status);

        [PreserveSig]
        int SetExecutionStatus(
            DEBUG_STATUS Status);

        [PreserveSig]
        int GetCodeLevel(
            out DEBUG_LEVEL Level);

        [PreserveSig]
        int SetCodeLevel(
            DEBUG_LEVEL Level);

        [PreserveSig]
        int GetEngineOptions(
            out DEBUG_ENGOPT Options);

        [PreserveSig]
        int AddEngineOptions(
            DEBUG_ENGOPT Options);

        [PreserveSig]
        int RemoveEngineOptions(
            DEBUG_ENGOPT Options);

        [PreserveSig]
        int SetEngineOptions(
            DEBUG_ENGOPT Options);

        [PreserveSig]
        int GetSystemErrorControl(
            out ERROR_LEVEL OutputLevel,
            out ERROR_LEVEL BreakLevel);

        [PreserveSig]
        int SetSystemErrorControl(
            ERROR_LEVEL OutputLevel,
            ERROR_LEVEL BreakLevel);

        [PreserveSig]
        int GetTextMacro(
            uint Slot,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint MacroSize);

        [PreserveSig]
        int SetTextMacro(
            uint Slot,
            [In][MarshalAs(UnmanagedType.LPStr)] string Macro);

        [PreserveSig]
        int GetRadix(
            out uint Radix);

        [PreserveSig]
        int SetRadix(
            uint Radix);

        [PreserveSig]
        int Evaluate(
            [In][MarshalAs(UnmanagedType.LPStr)] string Expression,
            DEBUG_VALUE_TYPE DesiredType,
            out DEBUG_VALUE Value,
            out uint RemainderIndex);

        [PreserveSig]
        int CoerceValue(
            DEBUG_VALUE In,
            DEBUG_VALUE_TYPE OutType,
            out DEBUG_VALUE Out);

        [PreserveSig]
        int CoerceValues(
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE[] In,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE_TYPE[] OutType,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_VALUE[] Out);

        [PreserveSig]
        int Execute(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string Command,
            DEBUG_EXECUTE Flags);

        [PreserveSig]
        int ExecuteCommandFile(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string CommandFile,
            DEBUG_EXECUTE Flags);

        [PreserveSig]
        int GetNumberBreakpoints(
            out uint Number);

        [PreserveSig]
        int GetBreakpointByIndex(
            uint Index,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugBreakpoint bp);

        [PreserveSig]
        int GetBreakpointById(
            uint Id,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugBreakpoint bp);

        [PreserveSig]
        int GetBreakpointParameters(
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Ids,
            uint Start,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_BREAKPOINT_PARAMETERS[] Params);

        [PreserveSig]
        int AddBreakpoint(
            DEBUG_BREAKPOINT_TYPE Type,
            uint DesiredId,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugBreakpoint Bp);

        [PreserveSig]
        int RemoveBreakpoint(
            [In][MarshalAs(UnmanagedType.Interface)]
            IDebugBreakpoint Bp);

        [PreserveSig]
        int AddExtension(
            [In][MarshalAs(UnmanagedType.LPStr)] string Path,
            uint Flags,
            out ulong Handle);

        [PreserveSig]
        int RemoveExtension(
            ulong Handle);

        [PreserveSig]
        int GetExtensionByPath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Path,
            out ulong Handle);

        [PreserveSig]
        int CallExtension(
            ulong Handle,
            [In][MarshalAs(UnmanagedType.LPStr)] string Function,
            [In][MarshalAs(UnmanagedType.LPStr)] string Arguments);

        [PreserveSig]
        int GetExtensionFunction(
            ulong Handle,
            [In][MarshalAs(UnmanagedType.LPStr)] string FuncName,
            out IntPtr Function);

        [PreserveSig]
        int GetWindbgExtensionApis32(
            ref WINDBG_EXTENSION_APIS Api);

        /* Must be In and Out as the nSize member has to be initialized */

        [PreserveSig]
        int GetWindbgExtensionApis64(
            ref WINDBG_EXTENSION_APIS Api);

        /* Must be In and Out as the nSize member has to be initialized */

        [PreserveSig]
        int GetNumberEventFilters(
            out uint SpecificEvents,
            out uint SpecificExceptions,
            out uint ArbitraryExceptions);

        [PreserveSig]
        int GetEventFilterText(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint TextSize);

        [PreserveSig]
        int GetEventFilterCommand(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint CommandSize);

        [PreserveSig]
        int SetEventFilterCommand(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Command);

        [PreserveSig]
        int GetSpecificFilterParameters(
            uint Start,
            uint Count,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_SPECIFIC_FILTER_PARAMETERS[] Params);

        [PreserveSig]
        int SetSpecificFilterParameters(
            uint Start,
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_SPECIFIC_FILTER_PARAMETERS[] Params);

        [PreserveSig]
        int GetSpecificEventFilterArgument(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint ArgumentSize);

        [PreserveSig]
        int SetSpecificEventFilterArgument(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Argument);

        [PreserveSig]
        int GetExceptionFilterParameters(
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Codes,
            uint Start,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_EXCEPTION_FILTER_PARAMETERS[] Params);

        [PreserveSig]
        int SetExceptionFilterParameters(
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_EXCEPTION_FILTER_PARAMETERS[] Params);

        [PreserveSig]
        int GetExceptionFilterSecondCommand(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint CommandSize);

        [PreserveSig]
        int SetExceptionFilterSecondCommand(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Command);

        [PreserveSig]
        int WaitForEvent(
            DEBUG_WAIT Flags,
            uint Timeout);

        [PreserveSig]
        int GetLastEventInformation(
            out DEBUG_EVENT Type,
            out uint ProcessId,
            out uint ThreadId,
            IntPtr ExtraInformation,
            uint ExtraInformationSize,
            out uint ExtraInformationUsed,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Description,
            int DescriptionSize,
            out uint DescriptionUsed);
    }
}
