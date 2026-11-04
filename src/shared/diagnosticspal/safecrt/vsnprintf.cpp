// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include <errno.h>
#include <limits.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
#include "internal_securecrt.h"
#include "mbusafecrt_internal.h"

static int VsnprintfHelper(char* string, size_t count, const char* format, va_list args)
{
    miniFILE stream;
    stream._cnt = count > INT_MAX ? INT_MAX : (int)count;
    stream._flag = _IOWRT | _IOSTRG;
    stream._ptr = stream._base = string;

    int result = _output_s(&stream, format, args);
    if (string == nullptr)
        return result;

    if (result >= 0 && _putc_nolock('\0', &stream) != EOF)
        return result;

    string[count - 1] = '\0';
    return stream._cnt < 0 ? -2 : -1;
}

extern "C" int __cdecl DiagnosticsVsnprintf_s(
    char* string,
    size_t sizeInBytes,
    size_t count,
    const char* format,
    va_list args)
{
    _VALIDATE_RETURN(format != nullptr, EINVAL, -1);
    if (count == 0 && string == nullptr && sizeInBytes == 0)
        return 0;

    _VALIDATE_RETURN(string != nullptr && sizeInBytes > 0, EINVAL, -1);

    errno_t savedErrno = errno;
    int result;
    if (sizeInBytes > count)
    {
        result = VsnprintfHelper(string, count + 1, format, args);
        if (result == -2)
        {
            _SECURECRT__FILL_STRING(string, sizeInBytes, count + 1);
            if (errno == ERANGE)
                errno = savedErrno;
            return -1;
        }
    }
    else
    {
        result = VsnprintfHelper(string, sizeInBytes, format, args);
        string[sizeInBytes - 1] = '\0';
        if (result == -2 && count == _TRUNCATE)
        {
            if (errno == ERANGE)
                errno = savedErrno;
            return -1;
        }
    }

    if (result < 0)
    {
        string[0] = '\0';
        _SECURECRT__FILL_STRING(string, sizeInBytes, 1);
        if (result == -2)
            _VALIDATE_RETURN(("Buffer too small" && 0), ERANGE, -1);
        return -1;
    }

    _SECURECRT__FILL_STRING(string, sizeInBytes, result + 1);
    return result;
}
