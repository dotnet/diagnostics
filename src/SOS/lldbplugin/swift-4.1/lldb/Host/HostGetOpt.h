//===-- GetOpt.h ------------------------------------------------*- C++ -*-===//
//
//                     The LLVM Compiler Infrastructure
//
// This file is distributed under the University of Illinois Open Source
// License. See LICENSE.TXT for details.
//
//===----------------------------------------------------------------------===//
#pragma once

#if !defined(_MSC_VER) && !defined(__NetBSD__)

#ifdef HOST_WINDOWS
#define _BSD_SOURCE // Required so that getopt.h defines optreset
#endif

#include <getopt.h>
#include <unistd.h>

#else

#include <lldb/Host/common/GetOptInc.h>

#endif
