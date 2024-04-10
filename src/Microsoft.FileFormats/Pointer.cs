// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    /// <summary>
    /// A pointer layout that can create pointers from integral storage types
    /// </summary>
    public class PointerLayout : LayoutBase
    {
        protected readonly ILayout _storageLayout;
        private readonly LayoutManager _layoutManager;
        private readonly Type _targetType;
        private ILayout _targetLayout;

        public ILayout TargetLayout => _targetLayout ??= _layoutManager.GetLayout(_targetType);

        public PointerLayout(LayoutManager layoutManager, Type pointerType, ILayout storageLayout, Type targetType) :
            base(pointerType, storageLayout.Size, storageLayout.NaturalAlignment)
        {
            _layoutManager = layoutManager;
            _storageLayout = storageLayout;
            _targetType = targetType;
        }
    }

    /// <summary>
    /// A pointer layout that can create pointers from the System.UInt64 storage type
    /// </summary>
    public class UInt64PointerLayout : PointerLayout
    {
        public UInt64PointerLayout(LayoutManager layoutManager, Type pointerType, ILayout storageLayout, Type targetType) :
            base(layoutManager, pointerType, storageLayout, targetType)
        {
            if (storageLayout.Type != typeof(ulong))
            {
                throw new ArgumentException("storageLayout must have System.UInt64 type");
            }
        }

        public override object Read(IAddressSpace dataSource, ulong position)
        {
            ulong value = (ulong)_storageLayout.Read(dataSource, position);
            Pointer p = (Pointer)Activator.CreateInstance(Type);
            p.Init(TargetLayout, value);
            return p;
        }
    }

    /// <summary>
    /// A pointer layout that can create pointers from the System.UInt32 storage type
    /// </summary>
    public class UInt32PointerLayout : PointerLayout
    {
        public UInt32PointerLayout(LayoutManager layoutManager, Type pointerType, ILayout storageLayout, Type targetType) :
            base(layoutManager, pointerType, storageLayout, targetType)
        {
            if (storageLayout.Type != typeof(uint))
            {
                throw new ArgumentException("storageLayout must have System.UInt32 type");
            }
        }

        public override object Read(IAddressSpace dataSource, ulong position)
        {
            ulong value = (uint)_storageLayout.Read(dataSource, position);
            Pointer p = (Pointer)Activator.CreateInstance(Type);
            p.Init(TargetLayout, value);
            return p;
        }
    }

    /// <summary>
    /// A pointer layout that can create pointers from the SizeT storage type
    /// </summary>
    public class SizeTPointerLayout : PointerLayout
    {
        public SizeTPointerLayout(LayoutManager layoutManager, Type pointerType, ILayout storageLayout, Type targetType) :
            base(layoutManager, pointerType, storageLayout, targetType)
        {
            if (storageLayout.Type != typeof(SizeT))
            {
                throw new ArgumentException("storageLayout must have SizeT type");
            }
        }

        public override object Read(IAddressSpace dataSource, ulong position)
        {
            ulong value = (SizeT)_storageLayout.Read(dataSource, position);
            Pointer p = (Pointer)Activator.CreateInstance(Type);
            p.Init(TargetLayout, value);
            return p;
        }
    }

    public class Pointer
    {
        public ulong Value;
        public bool IsNull
        {
            get { return Value == 0; }
        }

        public override string ToString()
        {
            return "0x" + Value.ToString("x");
        }

        public static implicit operator ulong (Pointer instance)
        {
            return instance.Value;
        }

        internal void Init(ILayout targetLayout, ulong value)
        {
            _targetLayout = targetLayout;
            Value = value;
        }

        protected ILayout _targetLayout;
    }

    /// <summary>
    /// A pointer that can be dereferenced to produce another object
    /// </summary>
    /// <typeparam name="TargetType">The type of object that is produced by dereferencing the pointer</typeparam>
    /// <typeparam name="StorageType">The type that determines how the pointer's underlying address value is parsed</typeparam>
    public class Pointer<TargetType, StorageType> : Pointer
    {
        /// <summary>
        /// Read an object of _TargetType_ from the _addressSpace_
        /// </summary>
        public TargetType Dereference(IAddressSpace addressSpace)
        {
            return Element(addressSpace, 0);
        }

        /// <summary>
        /// Read the array element _index_ from an array in _addressSpace_
        /// </summary>
        public TargetType Element(IAddressSpace addressSpace, uint index)
        {
            if (Value != 0)
            {
                return (TargetType)_targetLayout.Read(addressSpace, Value + index * _targetLayout.Size);
            }
            return default;
        }
    }

    public static partial class LayoutManagerExtensions
    {
        /// <summary>
        /// Adds support for reading types derived from Pointer<,>
        /// </summary>
        public static LayoutManager AddPointerTypes(this LayoutManager layouts)
        {
            layouts.AddLayoutProvider(GetPointerLayout);
            return layouts;
        }

        private static ILayout GetPointerLayout(Type pointerType, LayoutManager layoutManager)
        {
            if (!typeof(Pointer).GetTypeInfo().IsAssignableFrom(pointerType))
            {
                return null;
            }
            Type curPointerType = pointerType;
            TypeInfo genericPointerTypeInfo = null;
            while (curPointerType != typeof(Pointer))
            {
                TypeInfo curPointerTypeInfo = curPointerType.GetTypeInfo();
                if (curPointerTypeInfo.IsGenericType && curPointerTypeInfo.GetGenericTypeDefinition() == typeof(Pointer<,>))
                {
                    genericPointerTypeInfo = curPointerTypeInfo;
                    break;
                }
                curPointerType = curPointerTypeInfo.BaseType;
            }
            if (genericPointerTypeInfo == null)
            {
                throw new LayoutException("Pointer types must be derived from Pointer<,,>");
            }
            Type targetType = genericPointerTypeInfo.GetGenericArguments()[0];
            Type storageType = genericPointerTypeInfo.GetGenericArguments()[1];
            ILayout storageLayout = layoutManager.GetLayout(storageType);

            // Unfortunately the storageLayout.Read returns a boxed object that can't be
            // casted to a ulong without first being unboxed. These three Pointer layout
            // types are identical other than unboxing to a different type. Generics
            // doesn't work, there is no constraint that ensures the type parameter defines
            // a casting operator to ulong. Specifying a Func<object,ulong> parameter
            // would work, but I opted to write each class separately so that we don't
            // pay the cost of an extra delegate invocation for each pointer read. It
            // may be premature optimization, but the complexity of it should be relatively
            // constrained within this file at least.

            if (storageLayout.Type == typeof(SizeT))
            {
                return new SizeTPointerLayout(layoutManager, pointerType, storageLayout, targetType);
            }
            else if (storageLayout.Type == typeof(ulong))
            {
                return new UInt64PointerLayout(layoutManager, pointerType, storageLayout, targetType);
            }
            else if (storageLayout.Type == typeof(uint))
            {
                return new UInt32PointerLayout(layoutManager, pointerType, storageLayout, targetType);
            }
            else
            {
                throw new LayoutException("Pointer types must have a storage type of SizeT, ulong, or uint");
            }
        }
    }
}
