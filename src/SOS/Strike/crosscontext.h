// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

/// X86 Context
#define X86_SIZE_OF_80387_REGISTERS      80
#define X86_MAXIMUM_SUPPORTED_EXTENSION     512

typedef struct {
    DWORD   ControlWord;
    DWORD   StatusWord;
    DWORD   TagWord;
    DWORD   ErrorOffset;
    DWORD   ErrorSelector;
    DWORD   DataOffset;
    DWORD   DataSelector;
    BYTE    RegisterArea[X86_SIZE_OF_80387_REGISTERS];
    DWORD   Cr0NpxState;
} X86_FLOATING_SAVE_AREA;

typedef struct {

    DWORD ContextFlags;
    DWORD   Dr0;
    DWORD   Dr1;
    DWORD   Dr2;
    DWORD   Dr3;
    DWORD   Dr6;
    DWORD   Dr7;

    X86_FLOATING_SAVE_AREA FloatSave;

    DWORD   SegGs;
    DWORD   SegFs;
    DWORD   SegEs;
    DWORD   SegDs;

    DWORD   Edi;
    DWORD   Esi;
    DWORD   Ebx;
    DWORD   Edx;
    DWORD   Ecx;
    DWORD   Eax;

    DWORD   Ebp;
    DWORD   Eip;
    DWORD   SegCs;
    DWORD   EFlags;
    DWORD   Esp;
    DWORD   SegSs;

    BYTE    ExtendedRegisters[X86_MAXIMUM_SUPPORTED_EXTENSION];

} X86_CONTEXT;

typedef struct {
    ULONGLONG Low;
    LONGLONG High;
} M128A_XPLAT;


/// AMD64 Context
typedef struct {
    WORD   ControlWord;
    WORD   StatusWord;
    BYTE  TagWord;
    BYTE  Reserved1;
    WORD   ErrorOpcode;
    DWORD ErrorOffset;
    WORD   ErrorSelector;
    WORD   Reserved2;
    DWORD DataOffset;
    WORD   DataSelector;
    WORD   Reserved3;
    DWORD MxCsr;
    DWORD MxCsr_Mask;
    M128A_XPLAT FloatRegisters[8];

#if defined(_WIN64)
    M128A_XPLAT XmmRegisters[16];
    BYTE  Reserved4[96];
#else
    M128A_XPLAT XmmRegisters[8];
    BYTE  Reserved4[220];

    DWORD   Cr0NpxState;
#endif

} AMD64_XMM_SAVE_AREA32;

typedef struct {

    DWORD64 P1Home;
    DWORD64 P2Home;
    DWORD64 P3Home;
    DWORD64 P4Home;
    DWORD64 P5Home;
    DWORD64 P6Home;

    DWORD ContextFlags;
    DWORD MxCsr;

    WORD   SegCs;
    WORD   SegDs;
    WORD   SegEs;
    WORD   SegFs;
    WORD   SegGs;
    WORD   SegSs;
    DWORD EFlags;

    DWORD64 Dr0;
    DWORD64 Dr1;
    DWORD64 Dr2;
    DWORD64 Dr3;
    DWORD64 Dr6;
    DWORD64 Dr7;

    DWORD64 Rax;
    DWORD64 Rcx;
    DWORD64 Rdx;
    DWORD64 Rbx;
    DWORD64 Rsp;
    DWORD64 Rbp;
    DWORD64 Rsi;
    DWORD64 Rdi;
    DWORD64 R8;
    DWORD64 R9;
    DWORD64 R10;
    DWORD64 R11;
    DWORD64 R12;
    DWORD64 R13;
    DWORD64 R14;
    DWORD64 R15;

    DWORD64 Rip;

    union {
        AMD64_XMM_SAVE_AREA32 FltSave;
        struct {
            M128A_XPLAT Header[2];
            M128A_XPLAT Legacy[8];
            M128A_XPLAT Xmm0;
            M128A_XPLAT Xmm1;
            M128A_XPLAT Xmm2;
            M128A_XPLAT Xmm3;
            M128A_XPLAT Xmm4;
            M128A_XPLAT Xmm5;
            M128A_XPLAT Xmm6;
            M128A_XPLAT Xmm7;
            M128A_XPLAT Xmm8;
            M128A_XPLAT Xmm9;
            M128A_XPLAT Xmm10;
            M128A_XPLAT Xmm11;
            M128A_XPLAT Xmm12;
            M128A_XPLAT Xmm13;
            M128A_XPLAT Xmm14;
            M128A_XPLAT Xmm15;
        } DUMMYSTRUCTNAME;
    } DUMMYUNIONNAME;

    M128A_XPLAT VectorRegister[26];
    DWORD64 VectorControl;

    DWORD64 DebugControl;
    DWORD64 LastBranchToRip;
    DWORD64 LastBranchFromRip;
    DWORD64 LastExceptionToRip;
    DWORD64 LastExceptionFromRip;

} AMD64_CONTEXT;

typedef struct{
    __int64 LowPart;
    __int64 HighPart;
} FLOAT128_XPLAT;


/// ARM Context
#define ARM_MAX_BREAKPOINTS_CONST     8
#define ARM_MAX_WATCHPOINTS_CONST     1
typedef DECLSPEC_ALIGN(8) struct {

    DWORD ContextFlags;

    DWORD R0;
    DWORD R1;
    DWORD R2;
    DWORD R3;
    DWORD R4;
    DWORD R5;
    DWORD R6;
    DWORD R7;
    DWORD R8;
    DWORD R9;
    DWORD R10;
    DWORD R11;
    DWORD R12;

    DWORD Sp;
    DWORD Lr;
    DWORD Pc;
    DWORD Cpsr;

    DWORD Fpscr;
    DWORD Padding;
    union {
        M128A_XPLAT Q[16];
        ULONGLONG D[32];
        DWORD S[32];
    } DUMMYUNIONNAME;

    DWORD Bvr[ARM_MAX_BREAKPOINTS_CONST];
    DWORD Bcr[ARM_MAX_BREAKPOINTS_CONST];
    DWORD Wvr[ARM_MAX_WATCHPOINTS_CONST];
    DWORD Wcr[ARM_MAX_WATCHPOINTS_CONST];

    DWORD Padding2[2];

} ARM_CONTEXT;

// On ARM this mask is or'ed with the address of code to get an instruction pointer
#ifndef THUMB_CODE
#define THUMB_CODE 1
#endif

///ARM64 Context
#define ARM64_MAX_BREAKPOINTS     8
#define ARM64_MAX_WATCHPOINTS     2
typedef struct {

    DWORD ContextFlags;
    DWORD Cpsr;       // NZVF + DAIF + CurrentEL + SPSel
    union {
        struct {
            DWORD64 X0;
            DWORD64 X1;
            DWORD64 X2;
            DWORD64 X3;
            DWORD64 X4;
            DWORD64 X5;
            DWORD64 X6;
            DWORD64 X7;
            DWORD64 X8;
            DWORD64 X9;
            DWORD64 X10;
            DWORD64 X11;
            DWORD64 X12;
            DWORD64 X13;
            DWORD64 X14;
            DWORD64 X15;
            DWORD64 X16;
            DWORD64 X17;
            DWORD64 X18;
            DWORD64 X19;
            DWORD64 X20;
            DWORD64 X21;
            DWORD64 X22;
            DWORD64 X23;
            DWORD64 X24;
            DWORD64 X25;
            DWORD64 X26;
            DWORD64 X27;
            DWORD64 X28;
       };

       DWORD64 X[29];
   };

   DWORD64 Fp;
   DWORD64 Lr;
   DWORD64 Sp;
   DWORD64 Pc;


   M128A_XPLAT V[32];
   DWORD Fpcr;
   DWORD Fpsr;

   DWORD Bcr[ARM64_MAX_BREAKPOINTS];
   DWORD64 Bvr[ARM64_MAX_BREAKPOINTS];
   DWORD Wcr[ARM64_MAX_WATCHPOINTS];
   DWORD64 Wvr[ARM64_MAX_WATCHPOINTS];

} ARM64_CONTEXT;

///RISCV64 Context
#define RISCV64_MAX_BREAKPOINTS     8
#define RISCV64_MAX_WATCHPOINTS     2
typedef struct {

    DWORD ContextFlags;

    DWORD64 R0;
    DWORD64 Ra;
    DWORD64 Sp;
    DWORD64 Gp;
    DWORD64 Tp;
    DWORD64 T0;
    DWORD64 T1;
    DWORD64 T2;
    DWORD64 Fp;
    DWORD64 S1;
    DWORD64 A0;
    DWORD64 A1;
    DWORD64 A2;
    DWORD64 A3;
    DWORD64 A4;
    DWORD64 A5;
    DWORD64 A6;
    DWORD64 A7;
    DWORD64 S2;
    DWORD64 S3;
    DWORD64 S4;
    DWORD64 S5;
    DWORD64 S6;
    DWORD64 S7;
    DWORD64 S8;
    DWORD64 S9;
    DWORD64 S10;
    DWORD64 S11;
    DWORD64 T3;
    DWORD64 T4;
    DWORD64 T5;
    DWORD64 T6;
    DWORD64 Pc;

    ULONGLONG F[32];
    DWORD Fcsr;

    DWORD Padding[3];

} RISCV64_CONTEXT;

///LOONGARCH64 Context
#define LOONGARCH64_MAX_BREAKPOINTS     8
#define LOONGARCH64_MAX_WATCHPOINTS     2
typedef struct {

    DWORD ContextFlags;

    DWORD64 R0;
    DWORD64 Ra;
    DWORD64 Tp;
    DWORD64 Sp;
    DWORD64 A0;
    DWORD64 A1;
    DWORD64 A2;
    DWORD64 A3;
    DWORD64 A4;
    DWORD64 A5;
    DWORD64 A6;
    DWORD64 A7;
    DWORD64 T0;
    DWORD64 T1;
    DWORD64 T2;
    DWORD64 T3;
    DWORD64 T4;
    DWORD64 T5;
    DWORD64 T6;
    DWORD64 T7;
    DWORD64 T8;
    DWORD64 X0;
    DWORD64 Fp;
    DWORD64 S0;
    DWORD64 S1;
    DWORD64 S2;
    DWORD64 S3;
    DWORD64 S4;
    DWORD64 S5;
    DWORD64 S6;
    DWORD64 S7;
    DWORD64 S8;
    DWORD64 Pc;

    //
    // Floating Point Registers: FPR64/LSX/LASX.
    //
    ULONGLONG F[4*32];
    DWORD64 Fcc;
    DWORD   Fcsr;

} LOONGARCH64_CONTEXT;

typedef struct _CROSS_PLATFORM_CONTEXT {

    _CROSS_PLATFORM_CONTEXT() {}

    union {
        X86_CONTEXT       X86Context;
        AMD64_CONTEXT     Amd64Context;
        ARM_CONTEXT       ArmContext;
        ARM64_CONTEXT     Arm64Context;
        RISCV64_CONTEXT   RiscV64Context;
        LOONGARCH64_CONTEXT   LoongArch64Context;
    };

} CROSS_PLATFORM_CONTEXT, *PCROSS_PLATFORM_CONTEXT;

