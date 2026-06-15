// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if defined(__GNUC__) && !defined(__clang__) && defined(TARGET_SUNOS)
#define RETAIN __attribute__((used))
__asm__(".pushsection .init_array; .reloc ., R_X86_64_NONE, sccsid; .popsection");
#else
#define RETAIN __attribute__((used, retain))
#endif

static char sccsid[] RETAIN = "@(#)No version information produced";
