// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    /// <summary>
    /// A TStruct acts as a target-independent description of a structured sequence of bytes in
    /// the input being parsed.
    /// </summary>
    /// <remarks>
    /// TStructs primarily declare public instance fields. The type of each field must be a type
    /// for which a ILayout exists within the LayoutManager used to read it.
    ///
    /// Although a LayoutManager isn't required to have any particular types, it is often configured to support
    /// at least these types:
    ///
    ///   Byte/SByte/[U]Int[16/32/64]    - One of the fixed-size integral types. (In particular, *not* IntPtr)
    ///   Enum                           - Any enum whose underlying type is one of the fixed-size integral type.
    ///   TStruct                        - Another TStruct. Note that this describes a *nested* struct, not a pointer
    ///                                    to another struct.
    ///
    /// Binding to a LayoutManager object:
    /// --------------------------
    ///    A TStruct is not actually parsable until a LayoutManager produces an ILayout for it. The LayoutManager
    ///    provides the extra information (e.g. pointer size) that permit the parser to compute the final
    ///    offsets and size of the TStruct fields.
    ///
    /// Non-instance fields:
    /// --------------------
    ///    TStructs can contain methods, static or private fields and even nested classes if convenient. The parsing code
    ///    ignores them.
    ///
    /// </remarks>
    public abstract class TStruct
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class PackAttribute : Attribute
    {
        public uint Pack { get; private set; }

        public PackAttribute(uint pack)
        {
            Pack = pack;
        }
    }

    /// <summary>
    /// TLayouts expose one of these for each field that's mapped to the input
    /// </summary>
    public sealed class TField : IField
    {
        public TField(FieldInfo fieldInfo, ILayout layout, uint offset)
        {
            FieldInfo = fieldInfo;
            Layout = layout;
            Offset = offset;
        }

        public FieldInfo FieldInfo { get; private set; }
        public uint Offset { get; private set; }
        public uint Size { get { return Layout.Size; } }
        public uint NaturalAlignment { get { return Layout.NaturalAlignment; } }
        public ILayout Layout { get; private set; }
        public ILayout DeclaringLayout { get; set; }
        public string Name { get { return FieldInfo.Name; } }

        public object GetValue(TStruct tStruct)
        {
            return FieldInfo.GetValue(tStruct);
        }

        public override string ToString()
        {
            return DeclaringLayout.Type.FullName + "." + FieldInfo.Name + " [+0x" + Offset.ToString("x") + "]";
        }

        public void SetValue(TStruct tStruct, object newValue)
        {
            FieldInfo.SetValue(tStruct, newValue);
        }
    }

    /// <summary>
    /// The layout for a TStruct derived type
    /// </summary>
    public class TLayout : LayoutBase
    {
        public TLayout(Type type, uint size, uint naturalAlignment, uint sizeAsBaseType, IField[] fields) :
            base(type, size, naturalAlignment, sizeAsBaseType, fields)
        { }

        public override object Read(IAddressSpace dataTarget, ulong position)
        {
            TStruct blank = (TStruct)Activator.CreateInstance(Type);
            foreach (IField field in Fields)
            {
                object fieldValue = field.Layout.Read(dataTarget, position + field.Offset);
                field.SetValue(blank, fieldValue);
            }
            return blank;
        }
    }

    public static partial class LayoutManagerExtensions
    {
        /// <summary>
        /// Adds support for parsing types derived from TStruct. All the field types used within the TStruct types
        /// must also have layouts available from the LayoutManager.
        /// </summary>
        public static LayoutManager AddTStructTypes(this LayoutManager layouts)
        {
            return AddTStructTypes(layouts, null);
        }

        /// <summary>
        /// Adds support for parsing types derived from TStruct. All the field types used within the TStruct types
        /// must also have layouts available from the LayoutManager.
        /// </summary>
        /// <param name="layouts">The layout manager that will hold the new layout</param>
        /// <param name="enabledDefines">
        /// The set of defines that can be used to enabled optional fields decorated with the IfAttribute
        /// </param>
        public static LayoutManager AddTStructTypes(this LayoutManager layouts, IEnumerable<string> enabledDefines)
        {
            return AddReflectionTypes(layouts, enabledDefines, typeof(TStruct));
        }

        /// <summary>
        /// Adds support for parsing types derived from _requiredBaseType_ by using reflection to interpret their fields.
        /// All field types used within these types must also have layouts available from the LayoutManager.
        /// </summary>
        /// <param name="layouts"></param>
        /// <param name="enabledDefines">
        /// The set of defines that can be used to enabled optional fields decorated with the IfAttribute
        /// </param>
        /// <param name="requiredBaseType"></param>
        public static LayoutManager AddReflectionTypes(this LayoutManager layouts, IEnumerable<string> enabledDefines, Type requiredBaseType)
        {
            return layouts.AddReflectionTypes(enabledDefines, typeFilter: (type) => requiredBaseType.GetTypeInfo().IsAssignableFrom(type));
        }

        /// <summary>
        /// Adds support for parsing types filtered by typeFilter from by using reflection to interpret their fields.
        /// All field types used within these types must also have layouts available from the LayoutManager.
        /// </summary>
        /// <param name="layouts"></param>
        /// <param name="enabledDefines">
        /// The set of defines that can be used to enabled optional fields decorated with the IfAttribute
        /// </param>
        /// <param name="typeFilter">return true if reflection should be used to layout the type</param>
        public static LayoutManager AddReflectionTypes(this LayoutManager layouts, IEnumerable<string> enabledDefines, Func<Type, bool> typeFilter)
        {
            layouts.AddLayoutProvider((type, layoutManager) =>
            {
                if (!typeFilter(type))
                {
                    return null;
                }
                return GetTStructLayout(type, layoutManager, enabledDefines);
            });
            return layouts;
        }

        private static ILayout GetTStructLayout(Type tStructType, LayoutManager layoutManager, IEnumerable<string> enabledDefines)
        {
            enabledDefines ??= Array.Empty<string>();

            TypeInfo typeInfo = tStructType.GetTypeInfo();

            PackAttribute pack = typeInfo.GetCustomAttributes().Where(attr => attr is PackAttribute).Cast<PackAttribute>().SingleOrDefault();

            FieldInfo[] reflectionFields = typeInfo.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            reflectionFields = reflectionFields.OrderBy(f => f.MetadataToken).ToArray();
            reflectionFields = reflectionFields.Where(f => !f.DeclaringType.Equals(typeof(TStruct))).ToArray();
            reflectionFields = reflectionFields.Where(f => IsFieldIncludedInDefines(f, enabledDefines)).ToArray();
            TField[] tFields = new TField[reflectionFields.Length];

            uint alignCeiling = pack?.Pack ?? 8;
            uint biggestAlignmentSoFar = 1;
            uint curOffset = 0;

            ILayout parentLayout = null;
            Type baseType = typeInfo.BaseType;
            if (!baseType.Equals(typeof(TStruct)))
            {
                // Treat base type as first member.
                parentLayout = layoutManager.GetLayout(baseType);
                uint align = Math.Min(parentLayout.NaturalAlignment, alignCeiling);
                biggestAlignmentSoFar = Math.Max(biggestAlignmentSoFar, align);
                curOffset += parentLayout.SizeAsBaseType;
            }

            // build the field list
            for (int i = 0; i < reflectionFields.Length; i++)
            {
                ILayout fieldLayout = GetFieldLayout(reflectionFields[i], layoutManager);
                uint fieldSize = fieldLayout.Size;
                uint align = fieldLayout.NaturalAlignment;
                align = Math.Min(align, alignCeiling);
                biggestAlignmentSoFar = Math.Max(biggestAlignmentSoFar, align);
                curOffset = AlignUp(curOffset, align);
                tFields[i] = new TField(reflectionFields[i], fieldLayout, curOffset);
                curOffset += fieldSize;
            }
            curOffset = AlignUp(curOffset, biggestAlignmentSoFar);

            uint sizeAsBaseType = curOffset;
            if (curOffset == 0)
            {
                curOffset = 1;    // As with C++, zero-length struct not allowed (except as parent of another struct).
            }
            IField[] totalFields;
            if (parentLayout != null)
            {
                totalFields = parentLayout.Fields.Concat(tFields).ToArray();
            }
            else
            {
                totalFields = tFields;
            }
            TLayout layout = new(tStructType, curOffset, biggestAlignmentSoFar, sizeAsBaseType, totalFields);
            foreach (TField field in tFields)
            {
                field.DeclaringLayout = layout;
            }
            return layout;
        }

        private static bool IsFieldIncludedInDefines(FieldInfo fieldInfo, IEnumerable<string> enabledDefines)
        {
            IEnumerable<IfAttribute> attrs = fieldInfo.GetCustomAttributes<IfAttribute>();
            foreach (IfAttribute attr in attrs)
            {
                if (!enabledDefines.Contains(attr.DefineName))
                {
                    return false;
                }
            }
            return true;
        }

        private static ILayout GetFieldLayout(FieldInfo fieldInfo, LayoutManager layoutManager)
        {
            ILayout fieldLayout;
            Type fieldType = fieldInfo.FieldType;
            if (fieldType.IsArray)
            {
                ArraySizeAttribute ca = (ArraySizeAttribute)fieldInfo.GetCustomAttributes(typeof(ArraySizeAttribute)).FirstOrDefault();
                if (ca == null)
                {
                    throw new LayoutException("Array typed fields must use an ArraySize attribute to indicate their size");
                }
                fieldLayout = layoutManager.GetArrayLayout(fieldType, ca.NumElements);
            }
            else
            {
                fieldLayout = layoutManager.GetLayout(fieldType);
            }

            if (!fieldLayout.IsFixedSize)
            {
                throw new LayoutException(fieldInfo.Name + " is not a fixed size field. Only fixed size fields are supported in structures");
            }

            return fieldLayout;
        }

        private static uint AlignUp(uint p, uint align)
        {
            uint remainder = (p % align);
            if (remainder != 0)
            {
                p += (align - remainder);
            }
            return p;
        }
    }
}
