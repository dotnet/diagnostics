// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    internal sealed unsafe class MetadataImport : CallableCOMWrapper
    {
        public static readonly Guid IID_IMetaDataImport = new("7DAC8207-D3AE-4c75-9B67-92801A497D44");

        public MetadataImport(DacLibrary library, IntPtr pUnknown)
            : base(library?.OwningLibrary, IID_IMetaDataImport, pUnknown)
        {
        }

        private ref readonly IMetaDataImportVTable VTable => ref Unsafe.AsRef<IMetaDataImportVTable>(_vtable);

        public IEnumerable<int> EnumerateInterfaceImpls(int token)
        {
            IntPtr handle = IntPtr.Zero;
            int[] tokens = ArrayPool<int>.Shared.Rent(32);
            try
            {
                while (Enum(ref handle, token, tokens, tokens.Length, out int count) && count > 0)
                    for (int i = 0; i < count; i++)
                        yield return tokens[i];

                bool Enum(ref IntPtr handle, int token, int[] tokens, int length, out int count)
                {
                    fixed (int* ptr = tokens)
                    {
                        HResult hr = VTable.EnumInterfaceImpls(Self, ref handle, token, ptr, length, out count);
                        return hr;
                    }
                }
            }
            finally
            {
                if (handle != IntPtr.Zero)
                    CloseEnum(handle);

                ArrayPool<int>.Shared.Return(tokens);
            }
        }

        public MethodAttributes GetMethodAttributes(int token)
        {
            HResult hr = VTable.GetMethodProps(Self, token, out _, null, 0, out _, out MethodAttributes result, out _, out _, out _, out _);
            return hr ? result : default;
        }

        public uint GetRva(int token)
        {
            HResult hr = VTable.GetRVA(Self, token, out uint rva, out _);
            return hr ? rva : 0;
        }

        public HResult GetTypeDefProperties(int token, out string? name, out TypeAttributes attributes, out int mdParent)
        {
            HResult hr = VTable.GetTypeDefProps(Self, token, null, 0, out int needed, out attributes, out mdParent);
            if (!hr.IsOK)
            {
                name = null;
                return hr;
            }

            string nameResult = new('\0', needed - 1);
            fixed (char* nameResultPtr = nameResult)
                hr = VTable.GetTypeDefProps(Self, token, nameResultPtr, needed, out needed, out attributes, out mdParent);

            name = hr ? nameResult : null;
            return hr;
        }

        public HResult GetCustomAttributeByName(int token, string name, out IntPtr data, out uint cbData)
        {
            fixed (char* namePtr = name)
            {
                return VTable.GetCustomAttributeByName(Self, token, namePtr, out data, out cbData);
            }
        }

        public HResult GetFieldProps(int token, out string? name, out FieldAttributes attrs, out IntPtr ppvSigBlob, out int pcbSigBlob, out int pdwCPlusTypeFlag, out IntPtr ppValue)
        {
            HResult hr = VTable.GetFieldProps(Self, token, out int typeDef, null, 0, out int needed, out attrs, out ppvSigBlob, out pcbSigBlob, out pdwCPlusTypeFlag, out ppValue, out int pcchValue);
            if (!hr)
            {
                name = null;
                return hr;
            }

            string nameResult = new('\0', needed - 1);
            fixed (char* nameResultPtr = nameResult)
                hr = VTable.GetFieldProps(Self, token, out typeDef, nameResultPtr, needed, out needed, out attrs, out ppvSigBlob, out pcbSigBlob, out pdwCPlusTypeFlag, out ppValue, out pcchValue);

            name = hr ? nameResult : null;
            return hr;
        }

        public IEnumerable<int> EnumerateFields(int token)
        {
            IntPtr handle = IntPtr.Zero;
            int[] tokens = ArrayPool<int>.Shared.Rent(32);
            try
            {
                while (Enum(ref handle, token, tokens, tokens.Length, out int count) && count > 0)
                    for (int i = 0; i < count; i++)
                        yield return tokens[i];

                bool Enum(ref IntPtr handle, int token, int[] tokens, int length, out int count)
                {
                    fixed (int* ptr = tokens)
                    {
                        HResult hr = VTable.EnumFields(Self, ref handle, token, ptr, length, out count);
                        return hr;
                    }
                }
            }
            finally
            {
                if (handle != IntPtr.Zero)
                    CloseEnum(handle);

                ArrayPool<int>.Shared.Return(tokens);
            }
        }

        internal HResult GetTypeDefAttributes(int token, out TypeAttributes attrs)
        {
            return VTable.GetTypeDefProps(Self, token, null, 0, out _, out attrs, out _);
        }

        public string? GetTypeRefName(int token)
        {
            HResult hr = VTable.GetTypeRefProps(Self, token, out int scope, null, 0, out int needed);
            if (!hr.IsOK)
                return null;

            string nameResult = new('\0', needed - 1);
            fixed (char* nameResultPtr = nameResult)
            {
                hr = VTable.GetTypeRefProps(Self, token, out scope, nameResultPtr, needed, out needed);
                if (hr)
                    return nameResult;
            }

            return null;
        }

        public HResult GetNestedClassProperties(int token, out int enclosing)
        {
            return VTable.GetNestedClassProps(Self, token, out enclosing);
        }

        public HResult GetInterfaceImplProps(int token, out int mdClass, out int mdInterface)
        {
            return VTable.GetInterfaceImplProps(Self, token, out mdClass, out mdInterface);
        }

        private void CloseEnum(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                VTable.CloseEnum(Self, handle);
            }
        }

        public IEnumerable<int> EnumerateGenericParams(int token)
        {
            IntPtr handle = IntPtr.Zero;
            int[] tokens = ArrayPool<int>.Shared.Rent(32);
            try
            {
                while (Enum(ref handle, token, tokens, tokens.Length, out int count) && count > 0)
                    for (int i = 0; i < count; i++)
                        yield return tokens[i];

                bool Enum(ref IntPtr handle, int token, int[] tokens, int length, out int count)
                {
                    fixed (int* ptr = tokens)
                    {
                        HResult hr = VTable.EnumGenericParams(Self, ref handle, token, ptr, length, out count);
                        return hr;
                    }
                }
            }
            finally
            {
                if (handle != IntPtr.Zero)
                    CloseEnum(handle);

                ArrayPool<int>.Shared.Return(tokens);
            }
        }

        public bool GetGenericParamProps(int token, out int index, out GenericParameterAttributes attributes, [NotNullWhen(true)] out string? name)
        {
            // [NotNullWhen(true)] does not like returning HResult from this method
            name = null;

            HResult hr = VTable.GetGenericParamProps(Self, token, out index, out attributes, out int owner, out _, null, 0, out int needed);

            if (hr < 0)
                return false;

            string nameResult = new('\0', needed - 1);
            fixed (char* nameResultPtr = nameResult)
                hr = VTable.GetGenericParamProps(Self, token, out index, out attributes, out owner, out _, nameResultPtr, nameResult.Length + 1, out needed);

            if (hr < 0)
                return false;

            name = nameResult;
            return true;
        }

        public SigParser GetSigFromToken(int token)
        {
            HResult hr = VTable.GetSigFromToken(Self, token, out IntPtr sig, out int len);
            if (hr)
                return new SigParser(sig, len);

            return default;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct IMetaDataImportVTable
    {
        public readonly delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> CloseEnum;
        private readonly IntPtr CountEnum;
        private readonly IntPtr ResetEnum;
        private readonly IntPtr EnumTypeDefs;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ref IntPtr, int, int*, int, out int, int> EnumInterfaceImpls;
        private readonly IntPtr EnumTypeRefs;
        private readonly IntPtr FindTypeDefByName;
        private readonly IntPtr GetScopeProps;
        private readonly IntPtr GetModuleFromScope;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, char*, int, out int, out TypeAttributes, out int, int> GetTypeDefProps;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, out int, out int, int> GetInterfaceImplProps;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, out int, char*, int, out int, int> GetTypeRefProps;
        private readonly IntPtr ResolveTypeRef;
        private readonly IntPtr EnumMembers;
        private readonly IntPtr EnumMembersWithName;
        private readonly IntPtr EnumMethods;
        private readonly IntPtr EnumMethodsWithName;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ref IntPtr, int, int*, int, out int, int> EnumFields;
        private readonly IntPtr EnumFieldsWithName;
        private readonly IntPtr EnumParams;
        private readonly IntPtr EnumMemberRefs;
        private readonly IntPtr EnumMethodImpls;
        private readonly IntPtr EnumPermissionSets;
        private readonly IntPtr FindMember;
        private readonly IntPtr FindMethod;
        private readonly IntPtr FindField;
        private readonly IntPtr FindMemberRef;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, out int, StringBuilder?, int, out int, out MethodAttributes, out IntPtr, out uint, out uint, out uint, int> GetMethodProps;
        private readonly IntPtr GetMemberRefProps;
        private readonly IntPtr EnumProperties;
        private readonly IntPtr EnumEvents;
        private readonly IntPtr GetEventProps;
        private readonly IntPtr EnumMethodSemantics;
        private readonly IntPtr GetMethodSemantics;
        private readonly IntPtr GetClassLayout;
        private readonly IntPtr GetFieldMarshal;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, out uint, out uint, int> GetRVA;
        private readonly IntPtr GetPermissionSetProps;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, out IntPtr, out int, int> GetSigFromToken;
        private readonly IntPtr GetModuleRefProps;
        private readonly IntPtr EnumModuleRefs;
        private readonly IntPtr GetTypeSpecFromToken;
        private readonly IntPtr GetNameFromToken;
        private readonly IntPtr EnumUnresolvedMethods;
        private readonly IntPtr GetUserString;
        private readonly IntPtr GetPinvokeMap;
        private readonly IntPtr EnumSignatures;
        private readonly IntPtr EnumTypeSpecs;
        private readonly IntPtr EnumUserStrings;
        private readonly IntPtr GetParamForMethodIndex;
        private readonly IntPtr EnumCustomAttributes;
        private readonly IntPtr GetCustomAttributeProps;
        private readonly IntPtr FindTypeRef;
        private readonly IntPtr GetMemberProps;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, out int, char*, int, out int, out FieldAttributes, out IntPtr, out int, out int, out IntPtr, out int, int> GetFieldProps;
        private readonly IntPtr GetPropertyProps;
        private readonly IntPtr GetParamProps;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, char*, out IntPtr, out uint, int> GetCustomAttributeByName;
        private readonly IntPtr IsValidToken;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, out int, int> GetNestedClassProps;
        private readonly IntPtr GetNativeCallConvFromSig;
        private readonly IntPtr IsGlobal;

        // IMetaDataImport2
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ref IntPtr, int, int*, int, out int, int> EnumGenericParams;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, out int, out GenericParameterAttributes, out int, out int, char*, int, out int, int> GetGenericParamProps;
        private readonly IntPtr GetMethodSpecProps;
        private readonly IntPtr EnumGenericParamConstraints;
        private readonly IntPtr GetGenericParamConstraintProps;
        private readonly IntPtr GetPEKind;
        private readonly IntPtr GetVersionString;
        private readonly IntPtr EnumMethodSpecs;
    }
}