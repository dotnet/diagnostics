// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*++



Module Name:

    include/pal/threadsusp.hpp

Abstract:
    Declarations for thread suspension



--*/

#ifndef _PAL_THREADSUSP_HPP
#define _PAL_THREADSUSP_HPP

// Need this ifdef since this header is included by .c files so they can use the diagnostic function.
#ifdef __cplusplus

#include "pal/threadinfo.hpp"
#include "pal/thread.hpp"
#include "pal/printfcpp.hpp"
#include "pal/init.h"
#include <signal.h>
#include "pal/sharedmemory.h"

#endif // __cplusplus

#endif // _PAL_THREADSUSP_HPP
