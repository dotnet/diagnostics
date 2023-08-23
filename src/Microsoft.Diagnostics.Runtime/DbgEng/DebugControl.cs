// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DbgEng
{
    internal sealed unsafe class DebugControl : CallableCOMWrapper
    {
        public const int INITIAL_BREAK = 0x20;

        internal static readonly Guid IID_IDebugControl2 = new("d4366723-44df-4bed-8c7e-4c05424f4588");

        private readonly DebugSystemObjects _sys;

        public DebugControl(RefCountedFreeLibrary library, IntPtr pUnk, DebugSystemObjects sys)
            : base(library, IID_IDebugControl2, pUnk)
        {
            _sys = sys;
            SuppressRelease();
        }

        private ref readonly IDebugControlVTable VTable => ref Unsafe.AsRef<IDebugControlVTable>(_vtable);

        public IMAGE_FILE_MACHINE GetEffectiveProcessorType()
        {
            using IDisposable holder = _sys.Enter();
            int hr = VTable.GetEffectiveProcessorType(Self, out IMAGE_FILE_MACHINE result);
            if (hr == 0)
                return result;

            return IMAGE_FILE_MACHINE.UNKNOWN;
        }

        public bool IsPointer64Bit()
        {
            using IDisposable holder = _sys.Enter();
            int hr = VTable.IsPointer64Bit(Self);
            return hr == 0;
        }

        public HResult WaitForEvent(uint timeout)
        {
            using IDisposable holder = _sys.Enter();
            return VTable.WaitForEvent(Self, 0, timeout);
        }

        public HResult AddEngineOptions(int options)
        {
            using IDisposable holder = _sys.Enter();
            return VTable.AddEngineOptions(Self, options);
        }

        public DEBUG_FORMAT GetDumpFormat()
        {
            using IDisposable holder = _sys.Enter();
            HResult hr = VTable.GetDumpFormatFlags(Self, out DEBUG_FORMAT result);
            if (hr)
                return result;

            return DEBUG_FORMAT.DEFAULT;
        }

        public DEBUG_CLASS_QUALIFIER GetDebuggeeClassQualifier()
        {
            using IDisposable holder = _sys.Enter();
            HResult hr = VTable.GetDebuggeeType(Self, out _, out DEBUG_CLASS_QUALIFIER result);
            DebugOnly.Assert(hr.IsOK);
            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct IDebugControlVTable
    {
        public readonly IntPtr GetInterrupt;
        public readonly IntPtr SetInterrupt;
        public readonly IntPtr GetInterruptTimeout;
        public readonly IntPtr SetInterruptTimeout;
        public readonly IntPtr GetLogFile;
        public readonly IntPtr OpenLogFile;
        public readonly IntPtr CloseLogFile;
        public readonly IntPtr GetLogMask;
        public readonly IntPtr SetLogMask;
        public readonly IntPtr Input;
        public readonly IntPtr ReturnInput;
        public readonly IntPtr Output;
        public readonly IntPtr OutputVaList;
        public readonly IntPtr ControlledOutput;
        public readonly IntPtr ControlledOutputVaList;
        public readonly IntPtr OutputPrompt;
        public readonly IntPtr OutputPromptVaList;
        public readonly IntPtr GetPromptText;
        public readonly IntPtr OutputCurrentState;
        public readonly IntPtr OutputVersionInformation;
        public readonly IntPtr GetNotifyEventHandle;
        public readonly IntPtr SetNotifyEventHandle;
        public readonly IntPtr Assemble;
        public readonly IntPtr Disassemble;
        public readonly IntPtr GetDisassembleEffectiveOffset;
        public readonly IntPtr OutputDisassembly;
        public readonly IntPtr OutputDisassemblyLines;
        public readonly IntPtr GetNearInstruction;
        public readonly IntPtr GetStackTrace;
        public readonly IntPtr GetReturnOffset;
        public readonly IntPtr OutputStackTrace;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out uint, out DEBUG_CLASS_QUALIFIER, int> GetDebuggeeType;
        public readonly IntPtr GetActualProcessorType;
        public readonly IntPtr GetExecutingProcessorType;
        public readonly IntPtr GetNumberPossibleExecutingProcessorTypes;
        public readonly IntPtr GetPossibleExecutingProcessorTypes;
        public readonly IntPtr GetNumberProcessors;
        public readonly IntPtr GetSystemVersion;
        public readonly IntPtr GetPageSize;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int> IsPointer64Bit;
        public readonly IntPtr ReadBugCheckData;
        public readonly IntPtr GetNumberSupportedProcessorTypes;
        public readonly IntPtr GetSupportedProcessorTypes;
        public readonly IntPtr GetProcessorTypeNames;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out IMAGE_FILE_MACHINE, int> GetEffectiveProcessorType;
        public readonly IntPtr SetEffectiveProcessorType;
        public readonly IntPtr GetExecutionStatus;
        public readonly IntPtr SetExecutionStatus;
        public readonly IntPtr GetCodeLevel;
        public readonly IntPtr SetCodeLevel;
        public readonly IntPtr GetEngineOptions;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, int> AddEngineOptions;
        public readonly IntPtr RemoveEngineOptions;
        public readonly IntPtr SetEngineOptions;
        public readonly IntPtr GetSystemErrorControl;
        public readonly IntPtr SetSystemErrorControl;
        public readonly IntPtr GetTextMacro;
        public readonly IntPtr SetTextMacro;
        public readonly IntPtr GetRadix;
        public readonly IntPtr SetRadix;
        public readonly IntPtr Evaluate;
        public readonly IntPtr CoerceValue;
        public readonly IntPtr CoerceValues;
        public readonly IntPtr Execute;
        public readonly IntPtr ExecuteCommandFile;
        public readonly IntPtr GetNumberBreakpoints;
        public readonly IntPtr GetBreakpointByIndex;
        public readonly IntPtr GetBreakpointById;
        public readonly IntPtr GetBreakpointParameters;
        public readonly IntPtr AddBreakpoint;
        public readonly IntPtr RemoveBreakpoint;
        public readonly IntPtr AddExtension;
        public readonly IntPtr RemoveExtension;
        public readonly IntPtr GetExtensionByPath;
        public readonly IntPtr CallExtension;
        public readonly IntPtr GetExtensionFunction;
        public readonly IntPtr GetWindbgExtensionApis32;
        public readonly IntPtr GetWindbgExtensionApis64;
        public readonly IntPtr GetNumberEventFilters;
        public readonly IntPtr GetEventFilterText;
        public readonly IntPtr GetEventFilterCommand;
        public readonly IntPtr SetEventFilterCommand;
        public readonly IntPtr GetSpecificFilterParameters;
        public readonly IntPtr SetSpecificFilterParameters;
        public readonly IntPtr GetSpecificEventFilterArgument;
        public readonly IntPtr SetSpecificEventFilterArgument;
        public readonly IntPtr GetExceptionFilterParameters;
        public readonly IntPtr SetExceptionFilterParameters;
        public readonly IntPtr GetExceptionFilterSecondCommand;
        public readonly IntPtr SetExceptionFilterSecondCommand;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, uint, int> WaitForEvent;
        public readonly IntPtr GetLastEventInformation;

        public readonly IntPtr GetCurrentTimeDate;
        public readonly IntPtr GetCurrentSystemUpTime;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out DEBUG_FORMAT, int> GetDumpFormatFlags;
        public readonly IntPtr GetNumberTextReplacements;
        public readonly IntPtr GetTextReplacement;
        public readonly IntPtr SetTextReplacement;
        public readonly IntPtr RemoveTextReplacements;
        public readonly IntPtr OutputTextReplacements;
    }
}
