// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// warningcontrol.h
//
// KEEP THIS LIST SORTED!
//

#pragma once

#if defined(_MSC_VER)

#pragma warning(error   :4007)   // 'main' : must be __cdecl
#pragma warning(error   :4013)   // 'function' undefined - assuming extern returning int
#pragma warning(3       :4092)   // sizeof returns 'unsigned long'
#pragma warning(disable: 4100)   // unreferenced formal parameter
#pragma warning(error   :4102)   // "'%$S' : unreferenced label"
#pragma warning(3       :4121)   // structure is sensitive to alignment
#pragma warning(3       :4125)   // decimal digit in octal sequence
#pragma warning(disable :4127)   // conditional expression is constant
#pragma warning(3       :4130)   // logical operation on address of string constant
#pragma warning(3       :4132)   // const object should be initialized
#pragma warning(4       :4177)   // pragma data_seg s/b at global scope
#pragma warning(disable :4201)   // nonstandard extension used : nameless struct/union
#pragma warning(3       :4212)   // function declaration used ellipsis
#pragma warning(disable :4245)   // signed/unsigned mismatch
#pragma warning(disable :4291)   // delete not defined for new, c++ exception may cause leak
#pragma warning(disable :4311)   // pointer truncation from '%$S' to '%$S'
#pragma warning(disable :4312)   // '<function-style-cast>' : conversion from '%$S' to '%$S' of greater size
#pragma warning(disable :4430)   // missing type specifier: C++ doesn't support default-int
#pragma warning(disable :4477)   // format string '%$S' requires an argument of type '%$S', but variadic argument %d has type '%$S'
#pragma warning(3       :4530)   // C++ exception handler used, but unwind semantics are not enabled. Specify -GX
#pragma warning(error   :4551)   // Function call missing argument list
#pragma warning(error   :4806)   // unsafe operation involving type 'bool'
#pragma warning(disable :6255)   // Prefast: alloca indicates failure by raising a stack overflow exception

#if defined(_DEBUG) && (!defined(_MSC_FULL_VER) || (_MSC_FULL_VER <= 181040116))
// The CLR header file check.h, macro CHECK_MSG_EX, can create unreachable code if the LEAVE_DEBUG_ONLY_CODE
// macro is not empty (such as it is defined in contract.h) and the _RESULT macro expands to "return".
// Checked-in compilers used by the TFS-based desktop build (e.g., version 18.10.40116.8) started reporting
// unreachable code warnings when debugholder.h was changed to no longer #define "return" to something relatively
// complex. However, newer compilers, such as Visual Studio 2015, used to build the CLR from the open source
// GitHub repo, still do not report this warning. We don't want to disable this warning for open source build,
// which will use a newer compiler. Hence, only disable it for older compilers.
#pragma warning(disable :4702)   // unreachable code
#endif

#endif  // defined(_MSC_VER)
