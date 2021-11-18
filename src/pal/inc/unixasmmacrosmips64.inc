// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Copyright (c) Loongson Technology. All rights reserved.

#include "regdef_mips64.h"

.macro NESTED_ENTRY Name, Section, Handler
        LEAF_ENTRY \Name, \Section
        .ifnc \Handler, NoHandler
        .cfi_personality 0x1b, C_FUNC(\Handler) // 0x1b == DW_EH_PE_pcrel | DW_EH_PE_sdata4
        .endif
.endm

.macro NESTED_END Name, Section
        LEAF_END \Name, \Section
.endm

.macro PATCH_LABEL Name
        .global C_FUNC(\Name)
C_FUNC(\Name):
.endm

.macro LEAF_ENTRY Name, Section
        .global C_FUNC(\Name)
        .type \Name, %function
        .set push
        .set noat
C_FUNC(\Name):
        .cfi_startproc
.endm

.macro LEAF_END Name, Section
        .set pop
        .size \Name, .-\Name
        .cfi_endproc
.endm

.macro LEAF_END_MARKED Name, Section
C_FUNC(\Name\()_End):
        .global C_FUNC(\Name\()_End)
        LEAF_END \Name, \Section
.endm

.macro PREPARE_EXTERNAL_VAR Func, Name, HelperReg

        lui  \HelperReg, %hi(%neg(%gp_rel(\Func)))
        daddu  \HelperReg, \HelperReg, t9
        daddiu  \HelperReg, \HelperReg, %lo(%neg(%gp_rel(\Func)))
        ld  \HelperReg, %got_disp(\Name)(\HelperReg)

.endm

.macro PROLOG_STACK_ALLOC Size
        daddiu sp, sp, -\Size
        .cfi_adjust_cfa_offset \Size
.endm

.macro EPILOG_STACK_FREE Size
        daddiu sp, sp, \Size
        .cfi_adjust_cfa_offset -\Size
.endm

.macro EPILOG_STACK_RESTORE
        move  sp, fp
        .cfi_restore sp
.endm

.macro PROLOG_SAVE_REG reg, ofs
        sd \reg, \ofs(sp)
        .cfi_rel_offset \reg, \ofs
.endm

.macro PROLOG_SAVE_REG_PAIR reg1, reg2, ofs, __def_cfa_save=0
#ifdef FEATURE_LOONGSONISA
        //NOTE:The offset of gssq/gslq must be 16-bytes aligned.
        //     here ofs must be 16-bytes aligned.
        gssq \reg2, \reg1, \ofs(sp)
#else
        sd \reg1, \ofs(sp)
        sd \reg2, \ofs+8(sp)
#endif
        .cfi_rel_offset \reg1, \ofs
        .cfi_rel_offset \reg2, \ofs + 8
        .if (\__def_cfa_save ==  1)
           move fp, sp
           .cfi_def_cfa_register fp
        .endif
.endm

.macro PROLOG_SAVE_REG_PAIR_INDEXED reg1, reg2, ssize, __def_cfa_save=1
        daddiu  sp, sp, -\ssize
        .cfi_adjust_cfa_offset \ssize
#ifdef FEATURE_LOONGSONISA
        //NOTE:The offset of gssq/gslq must be 16-bytes aligned.
        //     here ofs must be 16-bytes aligned.
        gssq \reg2, \reg1, 0(sp)
#else
        sd \reg1, 0(sp)
        sd \reg2, 8(sp)
#endif
        .cfi_rel_offset \reg1, 0
        .cfi_rel_offset \reg2, 8
        .if (\__def_cfa_save ==  1)
          move fp, sp
          .cfi_def_cfa_register fp
        .endif
.endm

.macro EPILOG_RESTORE_REG reg, ofs
        ld \reg, \ofs(sp)
        .cfi_restore \reg
.endm

.macro EPILOG_RESTORE_REG_PAIR reg1, reg2, ofs
#ifdef FEATURE_LOONGSONISA
        gslq \reg2, \reg1, \ofs(sp)
#else
        ld \reg2, \ofs+8(sp)
        ld \reg1, \ofs(sp)
#endif
        .cfi_restore \reg2
        .cfi_restore \reg1
.endm

.macro EPILOG_RESTORE_REG_PAIR_INDEXED reg1, reg2, ssize
#ifdef FEATURE_LOONGSONISA
        gslq \reg2, \reg1, 0(sp)
#else
        ld \reg2, 8(sp)
        ld \reg1, 0(sp)
#endif
        .cfi_restore \reg2
        .cfi_restore \reg1

        daddiu  sp, sp, \ssize
        .cfi_adjust_cfa_offset -\ssize
.endm

.macro EPILOG_RETURN
        jr ra
        nop
.endm

.macro EMIT_BREAKPOINT
        break
.endm

.macro EPILOG_BRANCH Target
        b \Target
.endm

.macro EPILOG_BRANCH_REG reg
          //TODO: branch delay
        jr \reg

.endm

//-----------------------------------------------------------------------------
// The Following sets of SAVE_*_REGISTERS expect the memory to be reserved and
// base address to be passed in $reg
//

// Reserve 64 bytes of memory before calling  SAVE_CALLEESAVED_REGISTERS
.macro SAVE_CALLEESAVED_REGISTERS reg, ofs

        PROLOG_SAVE_REG_PAIR s0, s1, \ofs + 16
        PROLOG_SAVE_REG_PAIR s2, s3, \ofs + 32
        PROLOG_SAVE_REG_PAIR s4, s5, \ofs + 48
        PROLOG_SAVE_REG_PAIR s6, s7, \ofs + 64

        PROLOG_SAVE_REG gp, \ofs + 80

.endm

// Reserve 64 bytes of memory before calling  SAVE_ARGUMENT_REGISTERS
.macro SAVE_ARGUMENT_REGISTERS reg, ofs

#ifdef FEATURE_LOONGSONISA
        //NOTE:The offset of gssq/gslq must be 16-bytes aligned.
        //     here ofs must be 16-bytes aligned.
        gssq  a1, a0, \ofs(\reg)
        gssq  a3, a2, \ofs+16(\reg)
        gssq  a5, a4, \ofs+32(\reg)
        gssq  a7, a6, \ofs+48(\reg)
#else
        sd a0, \ofs(\reg)
        sd a1, \ofs+8(\reg)
        sd a2, \ofs+16(\reg)
        sd a3, \ofs+24(\reg)
        sd a4, \ofs+32(\reg)
        sd a5, \ofs+40(\reg)
        sd a6, \ofs+48(\reg)
        sd a7, \ofs+56(\reg)
#endif

.endm

// Reserve 64 bytes of memory before calling  SAVE_FLOAT_ARGUMENT_REGISTERS
.macro SAVE_FLOAT_ARGUMENT_REGISTERS reg, ofs

#ifdef FEATURE_LOONGSONISA
        //NOTE:The offset of gssqc1/gslqc1 must be 16-bytes aligned.
        //     here ofs must be 16-bytes aligned.
        gssqc1  $f13, $f12, \ofs(\reg)
        gssqc1  $f15, $f14, \ofs+16(\reg)
        gssqc1  $f17, $f16, \ofs+32(\reg)
        gssqc1  $f19, $f18, \ofs+48(\reg)
#else
        sdc1 $f12, \ofs(\reg)
        sdc1 $f13, \ofs+8(\reg)
        sdc1 $f14, \ofs+16(\reg)
        sdc1 $f15, \ofs+24(\reg)
        sdc1 $f16, \ofs+32(\reg)
        sdc1 $f17, \ofs+40(\reg)
        sdc1 $f18, \ofs+48(\reg)
        sdc1 $f19, \ofs+56(\reg)
#endif

.endm

// Reserve 64 bytes of memory before calling  SAVE_FLOAT_CALLEESAVED_REGISTERS
.macro SAVE_FLOAT_CALLEESAVED_REGISTERS reg, ofs

#ifdef FEATURE_LOONGSONISA
        //NOTE:The offset of gssqc1/gslqc1 must be 16-bytes aligned.
        //     here ofs must be 16-bytes aligned.
        gssqc1  $f25, $f24, \ofs(\reg)
        gssqc1  $f27, $f26, \ofs+16(\reg)
        gssqc1  $f29, $f28, \ofs+32(\reg)
        gssqc1  $f31, $f30, \ofs+48(\reg)
#else
        sdc1 $f24, \ofs(\reg)
        sdc1 $f25, \ofs+8(\reg)
        sdc1 $f26, \ofs+16(\reg)
        sdc1 $f27, \ofs+24(\reg)
        sdc1 $f28, \ofs+32(\reg)
        sdc1 $f29, \ofs+40(\reg)
        sdc1 $f30, \ofs+48(\reg)
        sdc1 $f31, \ofs+56(\reg)
#endif

.endm

.macro RESTORE_CALLEESAVED_REGISTERS reg, ofs

        EPILOG_RESTORE_REG gp, \ofs + 80

        EPILOG_RESTORE_REG_PAIR s6, s7, \ofs + 64
        EPILOG_RESTORE_REG_PAIR s4, s5, \ofs + 48
        EPILOG_RESTORE_REG_PAIR s2, s3, \ofs + 32
        EPILOG_RESTORE_REG_PAIR s0, s1, \ofs + 16
.endm

.macro RESTORE_ARGUMENT_REGISTERS reg, ofs

#ifdef FEATURE_LOONGSONISA
        //NOTE:The offset of gssq/gslq must be 16-bytes aligned.
        //     here ofs must be 16-bytes aligned.
        gslq  a7, a6, \ofs+48(\reg)
        gslq  a5, a4, \ofs+32(\reg)
        gslq  a3, a2, \ofs+16(\reg)
        gslq  a1, a0, \ofs(\reg)
#else
        ld a7, \ofs+56(\reg)
        ld a6, \ofs+48(\reg)
        ld a5, \ofs+40(\reg)
        ld a4, \ofs+32(\reg)
        ld a3, \ofs+24(\reg)
        ld a2, \ofs+16(\reg)
        ld a1, \ofs+8(\reg)
        ld a0, \ofs(\reg)
#endif

.endm

.macro RESTORE_FLOAT_ARGUMENT_REGISTERS reg, ofs

#ifdef FEATURE_LOONGSONISA
        gslqc1  $f19, $f18, \ofs+48(\reg)
        gslqc1  $f17, $f16, \ofs+32(\reg)
        gslqc1  $f15, $f14, \ofs+16(\reg)
        gslqc1  $f13, $f12, \ofs(\reg)
#else
        ldc1 $f19, \ofs+56(\reg)
        ldc1 $f18, \ofs+48(\reg)
        ldc1 $f17, \ofs+40(\reg)
        ldc1 $f16, \ofs+32(\reg)
        ldc1 $f15, \ofs+24(\reg)
        ldc1 $f14, \ofs+16(\reg)
        ldc1 $f13, \ofs+8(\reg)
        ldc1 $f12, \ofs(\reg)
#endif

.endm

.macro RESTORE_FLOAT_CALLEESAVED_REGISTERS reg, ofs

#ifdef FEATURE_LOONGSONISA
        //NOTE:The offset of gssqc1/gslqc1 must be 16-bytes aligned.
        //     here ofs must be 16-bytes aligned.
        gslqc1  $f25, $f24, \ofs(\reg)
        gslqc1  $f27, $f26, \ofs+16(\reg)
        gslqc1  $f29, $f28, \ofs+32(\reg)
        gslqc1  $f31, $f30, \ofs+48(\reg)
#else
        ldc1 $f24, \ofs(\reg)
        ldc1 $f25, \ofs+8(\reg)
        ldc1 $f26, \ofs+16(\reg)
        ldc1 $f27, \ofs+24(\reg)
        ldc1 $f28, \ofs+32(\reg)
        ldc1 $f29, \ofs+40(\reg)
        ldc1 $f30, \ofs+48(\reg)
        ldc1 $f31, \ofs+56(\reg)
#endif

.endm

//-----------------------------------------------------------------------------
// Define the prolog for a TransitionFrame-based method. This macro should be called first in the method and
// comprises the entire prolog.The locals must be 8 byte aligned
//
// Save_argument_registers:
//            GPR_a7
//            GPR_a6
//            GPR_a5
//            GPR_a4
//            GPR_a3
//            GPR_a2
//            GPR_a1
//            GPR_a0
//
// General Registers:
//            hi
//            lo
//            Pading
//            GPR_gp
//            GPR_s7
//            GPR_s6
//            GPR_s5
//            GPR_s4
//            GPR_s3
//            GPR_s2
//            GPR_s1
//            GPR_s0
//            GPR_ra
//            GPR_s8/fp
//
// Float Point:
//            FPR_f19
//            FPR_f18
//            FPR_f17
//            FPR_f16
//            FPR_f15
//            FPR_f14
//            FPR_f13
//            FPR_f12
// Extra:
//
.macro PROLOG_WITH_TRANSITION_BLOCK extraParameters = 0, extraLocals = 0, SaveFPRegs = 1

        __PWTB_SaveFPArgs = \SaveFPRegs

        __PWTB_FloatArgumentRegisters = \extraLocals

        .if ((__PWTB_FloatArgumentRegisters % 16) != 0)
            __PWTB_FloatArgumentRegisters = __PWTB_FloatArgumentRegisters + 8
        .endif

        __PWTB_TransitionBlock = __PWTB_FloatArgumentRegisters

        .if (__PWTB_SaveFPArgs == 1)
            __PWTB_TransitionBlock = __PWTB_TransitionBlock + SIZEOF__FloatArgumentRegisters
        .endif


        __PWTB_CalleeSavedRegisters = __PWTB_TransitionBlock
        __PWTB_ArgumentRegisters = __PWTB_TransitionBlock + 112

        // Including fp, ra, s0-s7, gp, pading, lo, hi and arguments. (1+1+8+1+1+2)*8 + 8*8.
        __PWTB_StackAlloc = __PWTB_TransitionBlock + 112 + 64
        PROLOG_STACK_ALLOC __PWTB_StackAlloc
        PROLOG_SAVE_REG_PAIR  fp, ra, __PWTB_CalleeSavedRegisters, 1

        // First, Spill argument registers.
        SAVE_ARGUMENT_REGISTERS        sp, __PWTB_ArgumentRegisters

        // Then, Spill callee saved registers.
        SAVE_CALLEESAVED_REGISTERS     sp, __PWTB_CalleeSavedRegisters

        // saving is f12-19.
        .if (__PWTB_SaveFPArgs == 1)
            SAVE_FLOAT_ARGUMENT_REGISTERS sp, __PWTB_FloatArgumentRegisters
        .endif

.endm

.macro EPILOG_WITH_TRANSITION_BLOCK_RETURN

        RESTORE_CALLEESAVED_REGISTERS sp, __PWTB_CalleeSavedRegisters

        EPILOG_RESTORE_REG_PAIR fp, ra, __PWTB_CalleeSavedRegisters

        EPILOG_STACK_FREE                 __PWTB_StackAlloc

        jr ra    //TODO: branch delay

.endm


//-----------------------------------------------------------------------------
// Provides a matching epilog to PROLOG_WITH_TRANSITION_BLOCK and ends by preparing for tail-calling.
// Since this is a tail call argument registers are restored.
//
.macro EPILOG_WITH_TRANSITION_BLOCK_TAILCALL

        .if (__PWTB_SaveFPArgs == 1)
            RESTORE_FLOAT_ARGUMENT_REGISTERS  sp, __PWTB_FloatArgumentRegisters
        .endif

        RESTORE_CALLEESAVED_REGISTERS     sp, __PWTB_CalleeSavedRegisters

        RESTORE_ARGUMENT_REGISTERS        sp, __PWTB_ArgumentRegisters

        EPILOG_RESTORE_REG_PAIR fp, ra, __PWTB_CalleeSavedRegisters

        EPILOG_STACK_FREE                 __PWTB_StackAlloc

.endm

// ------------------------------------------------------------------
// Macro to generate Redirection Stubs
//
// $reason : reason for redirection
//                     Eg. GCThreadControl
// NOTE: If you edit this macro, make sure you update GetCONTEXTFromRedirectedStubStackFrame.
// This function is used by both the personality routine and the debugger to retrieve the original CONTEXT.
.macro GenerateRedirectedHandledJITCaseStub reason

#if NOTYET
        GBLS __RedirectionStubFuncName
        GBLS __RedirectionStubEndFuncName
        GBLS __RedirectionFuncName
__RedirectionStubFuncName SETS "RedirectedHandledJITCaseFor":CC:"$reason":CC:"_Stub"
__RedirectionStubEndFuncName SETS "RedirectedHandledJITCaseFor":CC:"$reason":CC:"_StubEnd"
__RedirectionFuncName SETS "|?RedirectedHandledJITCaseFor":CC:"$reason":CC:"@Thread@@CAXXZ|"

        IMPORT $__RedirectionFuncName

        NESTED_ENTRY $__RedirectionStubFuncName
        daddiu sp, sp, -32                          // stack slot for CONTEXT * and padding
        PROLOG_SAVE_REG_PAIR    fp, ra, 8, 1
        sd t9, 24(sp)

        //REDIRECTSTUB_SP_OFFSET_CONTEXT is defined in asmconstants.h and is used in GetCONTEXTFromRedirectedStubStackFrame
        //If CONTEXT is not saved at 0 offset from SP it must be changed as well.
        ASSERT REDIRECTSTUB_SP_OFFSET_CONTEXT == 0

        //// Stack alignment. This check is necessary as this function can be
        //// entered before complete execution of the prolog of another function.
        //andi at, fp, #15
        //sub  sp, sp, at


        //
        // Save a copy of the redirect CONTEXT*.
        // This is needed for the debugger to unwind the stack.
        //
        lui  AT, %hi(%neg(%gp_rel($__RedirectionStubFuncName)))
        daddu  t0, AT, t9
        daddiu  AT, t0, %lo(%neg(%gp_rel($__RedirectionStubFuncName)))
        ld  t9, %call16(GetCurrentSavedRedirectContext)(AT)
        jalr t9

        sd v0, 0(sp)
        sd t9, 24(sp)

        //
        // Fetch the interrupted pc and save it as our return address.
        //
        ld a1, CONTEXT_PC(a0)
        sd a1, 8(fp)

        //
        // Call target, which will do whatever we needed to do in the context
        // of the target thread, and will RtlRestoreContext when it is done.
        //
        lui  AT, %hi(%neg(%gp_rel($__RedirectionStubFuncName)))
        daddu  t0, AT, t9
        daddiu  AT, t0, %lo(%neg(%gp_rel($__RedirectionStubFuncName)))
        ld  t9, %call16($__RedirectionFuncName)(AT)
        jalr t9

        EMIT_BREAKPOINT // Unreachable

// Put a label here to tell the debugger where the end of this function is.
$__RedirectionStubEndFuncName
        EXPORT $__RedirectionStubEndFuncName

        NESTED_END
#else
        EMIT_BREAKPOINT
#endif
.endm
