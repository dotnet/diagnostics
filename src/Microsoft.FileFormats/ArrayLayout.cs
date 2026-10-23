// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    [RequiresDynamicCode("Runtime array allocation requires dynamic code. Use ArrayLayout<T> instead.")]
    [RequiresUnreferencedCode("The array element type may be trimmed. Use ArrayLayout<T> instead.")]
    internal sealed class ArrayLayout : LayoutBase
    {
        public ArrayLayout(Type arrayType, ILayout elementLayout, uint numElements) :
            base(arrayType, numElements * elementLayout.Size, elementLayout.NaturalAlignment)
        {
            _elementLayout = elementLayout;
            _numElements = numElements;
        }

        public override object Read(IAddressSpace dataSource, ulong position)
        {
            ulong src = position;
            uint elementSize = _elementLayout.Size;
            if (_numElements > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException("length");
            }
            Array a = Array.CreateInstance(_elementLayout.Type, (int)_numElements);
            for (uint i = 0; i < _numElements; i++)
            {
                a.SetValue(_elementLayout.Read(dataSource, src), (int)i);
                src += elementSize;
            }
            return a;
        }

        private uint _numElements;
        private ILayout _elementLayout;
    }

    internal sealed class ArrayLayout<T> : LayoutBase
    {
        private readonly uint _numElements;
        private readonly ILayout _elementLayout;

        public ArrayLayout(ILayout elementLayout, uint numElements) :
            base(typeof(T[]), numElements * elementLayout.Size, elementLayout.NaturalAlignment)
        {
            _elementLayout = elementLayout;
            _numElements = numElements;
        }

        public override object Read(IAddressSpace dataSource, ulong position)
        {
            if (_numElements > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException("length");
            }
            T[] array = new T[(int)_numElements];
            ulong src = position;
            uint elementSize = _elementLayout.Size;
            for (uint i = 0; i < _numElements; i++)
            {
                array[i] = (T)_elementLayout.Read(dataSource, src);
                src += elementSize;
            }
            return array;
        }
    }
}
