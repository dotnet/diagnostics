// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
// TLS.H -
//

//
// Encapsulates TLS access for maximum performance. 
//

// ******************************************************************************
// WARNING!!!: This header is also used by the runtime repo.
// See: https://github.com/dotnet/runtime/blob/master/src/coreclr/src/inc/tls.h
// ******************************************************************************

#ifndef __tls_h__
#define __tls_h__

#define OFFSETOF__TLS__tls_CurrentThread         (0x0)
#define OFFSETOF__TLS__tls_EETlsData             (2*sizeof(void*))

#ifdef DBG_TARGET_64BIT
#define WINNT_OFFSETOF__TEB__ThreadLocalStoragePointer  0x58
#else
#define WINNT_OFFSETOF__TEB__ThreadLocalStoragePointer  0x2c
#endif

#endif // __tls_h__
