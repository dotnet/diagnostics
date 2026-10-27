// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "pal/palinternal.h"

#include <stdio.h>

#include "mbusafecrt_internal.h"

int _putc_nolock(char value, miniFILE* stream)
{
    stream->_cnt -= sizeof(char);
    if (stream->_cnt < 0)
        return EOF;

    *stream->_ptr = value;
    stream->_ptr += sizeof(char);
    return value;
}

int _putwc_nolock(char16_t value, miniFILE* stream)
{
    stream->_cnt -= sizeof(char16_t);
    if (stream->_cnt < 0)
        return WEOF;

    *reinterpret_cast<char16_t*>(stream->_ptr) = value;
    stream->_ptr += sizeof(char16_t);
    return value;
}

errno_t _safecrt_cfltcvt(double* value, char* buffer, size_t bufferSize, int type, int precision, int flags)
{
    char format[30];
    int written = snprintf(format, sizeof(format), (flags & 0x00080) != 0 ? "%%#.%d%c" : "%%.%d%c", precision, type);
    if (written < 0 || static_cast<size_t>(written) >= sizeof(format))
        return EINVAL;

    return snprintf(buffer, bufferSize, format, *value) < 0 ? EINVAL : 0;
}
