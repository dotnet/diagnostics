// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum _EXT_TDOP
    {
        EXT_TDOP_COPY,
        EXT_TDOP_RELEASE,
        EXT_TDOP_SET_FROM_EXPR,
        EXT_TDOP_SET_FROM_U64_EXPR,
        EXT_TDOP_GET_FIELD,
        EXT_TDOP_EVALUATE,
        EXT_TDOP_GET_TYPE_NAME,
        EXT_TDOP_OUTPUT_TYPE_NAME,
        EXT_TDOP_OUTPUT_SIMPLE_VALUE,
        EXT_TDOP_OUTPUT_FULL_VALUE,
        EXT_TDOP_HAS_FIELD,
        EXT_TDOP_GET_FIELD_OFFSET,
        EXT_TDOP_GET_ARRAY_ELEMENT,
        EXT_TDOP_GET_DEREFERENCE,
        EXT_TDOP_GET_TYPE_SIZE,
        EXT_TDOP_OUTPUT_TYPE_DEFINITION,
        EXT_TDOP_GET_POINTER_TO,
        EXT_TDOP_SET_FROM_TYPE_ID_AND_U64,
        EXT_TDOP_SET_PTR_FROM_TYPE_ID_AND_U64,
        EXT_TDOP_COUNT
    }
}
