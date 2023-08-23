// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// This is a representation of the metadata element type.  These values
    /// directly correspond with CLR's CorElementType.
    /// </summary>
    public enum ClrElementType
    {
        /// <summary>
        /// Not one of the other types.
        /// </summary>
        Unknown = 0x0,

        /// <summary>
        /// Void type.
        /// </summary>
        Void = 0x1,

        /// <summary>
        /// ELEMENT_TYPE_BOOLEAN
        /// </summary>
        Boolean = 0x2,

        /// <summary>
        /// ELEMENT_TYPE_CHAR
        /// </summary>
        Char = 0x3,

        /// <summary>
        /// ELEMENT_TYPE_I1
        /// </summary>
        Int8 = 0x4,

        /// <summary>
        /// ELEMENT_TYPE_U1
        /// </summary>
        UInt8 = 0x5,

        /// <summary>
        /// ELEMENT_TYPE_I2
        /// </summary>
        Int16 = 0x6,

        /// <summary>
        /// ELEMENT_TYPE_U2
        /// </summary>
        UInt16 = 0x7,

        /// <summary>
        /// ELEMENT_TYPE_I4
        /// </summary>
        Int32 = 0x8,

        /// <summary>
        /// ELEMENT_TYPE_U4
        /// </summary>
        UInt32 = 0x9,

        /// <summary>
        /// ELEMENT_TYPE_I8
        /// </summary>
        Int64 = 0xa,

        /// <summary>
        /// ELEMENT_TYPE_U8
        /// </summary>
        UInt64 = 0xb,

        /// <summary>
        /// ELEMENT_TYPE_R4
        /// </summary>
        Float = 0xc,

        /// <summary>
        /// ELEMENT_TYPE_R8
        /// </summary>
        Double = 0xd,

        /// <summary>
        /// ELEMENT_TYPE_STRING
        /// </summary>
        String = 0xe,

        /// <summary>
        /// ELEMENT_TYPE_PTR
        /// </summary>
        Pointer = 0xf,

        /// <summary>
        /// ELEMENT_TYPE_BYREF
        /// </summary>
        ByRef = 0x10,

        /// <summary>
        /// ELEMENT_TYPE_VALUETYPE
        /// </summary>
        Struct = 0x11,

        /// <summary>
        /// ELEMENT_TYPE_CLASS
        /// </summary>
        Class = 0x12,

        /// <summary>
        /// ELEMENT_TYPE_VAR
        /// </summary>
        Var = 0x13,

        /// <summary>
        /// ELEMENT_TYPE_ARRAY
        /// </summary>
        Array = 0x14,

        /// <summary>
        /// ELEMENT_TYPE_GENERICINST
        /// </summary>
        GenericInstantiation = 0x15,

        /// <summary>
        /// ELEMENT_TYPE_I
        /// </summary>
        NativeInt = 0x18,

        /// <summary>
        /// ELEMENT_TYPE_U
        /// </summary>
        NativeUInt = 0x19,

        /// <summary>
        /// ELEMENT_TYPE_FNPTR
        /// </summary>
        FunctionPointer = 0x1B,

        /// <summary>
        /// ELEMENT_TYPE_OBJECT
        /// </summary>
        Object = 0x1C,

        /// <summary>
        /// ELEMENT_TYPE_MVAR
        /// </summary>
        MVar = 0x1e,

        /// <summary>
        /// ELEMENT_TYPE_SZARRAY
        /// </summary>
        SZArray = 0x1D
    }
}
