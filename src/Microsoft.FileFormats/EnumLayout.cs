// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    public class EnumLayout : LayoutBase
    {
        public EnumLayout(Type enumType, ILayout underlyingIntegralLayout) :
            base(enumType, underlyingIntegralLayout.Size, underlyingIntegralLayout.NaturalAlignment)
        {
            _underlyingIntegralLayout = underlyingIntegralLayout;
        }

        public override object Read(IAddressSpace dataSource, ulong position)
        {
            return _underlyingIntegralLayout.Read(dataSource, position);
        }

        private ILayout _underlyingIntegralLayout;
    }

    public static partial class LayoutManagerExtensions
    {
        public static LayoutManager AddEnumTypes(this LayoutManager layoutManager)
        {
            layoutManager.AddLayoutProvider(GetEnumLayout);
            return layoutManager;
        }

        private static ILayout GetEnumLayout(Type enumType, LayoutManager layoutManager)
        {
            if (!enumType.GetTypeInfo().IsEnum)
            {
                return null;
            }
            Type elementType = enumType.GetTypeInfo().GetEnumUnderlyingType();
            return new EnumLayout(enumType, layoutManager.GetLayout(elementType));
        }
    }
}
