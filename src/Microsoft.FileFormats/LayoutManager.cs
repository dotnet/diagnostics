// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    /// <summary>
    /// A container that can provide ILayout instances for Types.
    /// </summary>
    public class LayoutManager
    {
        private Dictionary<Type, ILayout> _layouts = new();
        private List<Func<Type, LayoutManager, ILayout>> _layoutProviders = new();
        private Dictionary<Tuple<Type, uint>, ILayout> _arrayLayouts = new();
        private readonly Dictionary<Type, Func<LayoutManager, ILayout>> _layoutFactories = new();
        private readonly Dictionary<Type, Func<LayoutManager, uint, ILayout>> _arrayFactories = new();

        public LayoutManager() { }

        public void AddLayout(ILayout layout)
        {
            if (_layoutFactories.ContainsKey(layout.Type))
            {
                throw new ArgumentException("A layout is already registered for " + layout.Type.FullName, nameof(layout));
            }
            _layouts.Add(layout.Type, layout);
        }

        public void AddLayoutProvider(Func<Type, LayoutManager, ILayout> layoutProvider)
        {
            _layoutProviders.Add(layoutProvider);
        }

        [RequiresDynamicCode("Runtime array allocation requires dynamic code. Use GetArrayLayoutForElement<T> or RegisterArray<T> instead.")]
        [RequiresUnreferencedCode("The array element type's layout members may be trimmed. Register array element layouts explicitly.")]
        public ILayout GetArrayLayout(Type arrayType, uint numElements)
        {
            if (!arrayType.IsArray)
            {
                throw new ArgumentException("The type parameter must be an array");
            }
            if (arrayType.GetArrayRank() != 1)
            {
                throw new ArgumentException("Multidimensional arrays are not supported");
            }

            ILayout layout;
            Tuple<Type, uint> key = new(arrayType, numElements);
            if (!_arrayLayouts.TryGetValue(key, out layout))
            {
                Type elemType = arrayType.GetElementType();
                layout = new ArrayLayout(arrayType, GetLayout(elemType), numElements);
                _arrayLayouts.Add(key, layout);
            }
            return layout;
        }

        /// <summary>Gets a layout for the specified array type.</summary>
        [RequiresDynamicCode("Runtime array allocation requires dynamic code. Use GetArrayLayoutForElement<T> instead.")]
        [RequiresUnreferencedCode("The array element type's layout members may be trimmed. Register array element layouts explicitly.")]
        public ILayout GetArrayLayout<T>(uint numElements)
        {
            return GetArrayLayout(typeof(T), numElements);
        }

        /// <summary>Gets a layout for an array of the statically known element type.</summary>
        public ILayout GetArrayLayoutForElement<T>(uint numElements)
        {
            Tuple<Type, uint> key = new(typeof(T[]), numElements);
            if (!_arrayLayouts.TryGetValue(key, out ILayout layout))
            {
                layout = CreateArrayLayout<T>(numElements);
                _arrayLayouts.Add(key, layout);
            }
            return layout;
        }

        /// <summary>Registers typed allocation for fixed array fields whose elements have a layout.</summary>
        public LayoutManager RegisterArray<T>()
        {
            _arrayFactories.Add(typeof(T[]), (manager, count) => manager.CreateArrayLayout<T>(count));
            return this;
        }

        private ILayout CreateArrayLayout<T>(uint count)
        {
            return new ArrayLayout<T>(GetLayout<T>(), count);
        }

        internal ILayout GetRegisteredArrayLayout(Type arrayType, uint count)
        {
            Tuple<Type, uint> key = new(arrayType, count);
            if (!_arrayLayouts.TryGetValue(key, out ILayout layout))
            {
                if (!_arrayFactories.TryGetValue(arrayType, out Func<LayoutManager, uint, ILayout> factory))
                {
                    throw new LayoutException("No array allocator registered for " + arrayType.FullName + ". Use RegisterArray<T>.");
                }
                layout = factory(this, count);
                _arrayLayouts.Add(key, layout);
            }
            return layout;
        }

        internal void RegisterLayoutFactory(Type type, Func<LayoutManager, ILayout> factory)
        {
            if (_layouts.ContainsKey(type))
            {
                throw new ArgumentException("A layout already exists for " + type.FullName, nameof(type));
            }
            _layoutFactories.Add(type, factory);
        }

        public ILayout GetLayout<T>()
        {
            return GetLayout(typeof(T));
        }

        public ILayout GetLayout(Type t)
        {
            ILayout layout;
            if (!_layouts.TryGetValue(t, out layout))
            {
                if (_layoutFactories.TryGetValue(t, out Func<LayoutManager, ILayout> factory))
                {
                    layout = factory(this);
                }
                else
                {
                    foreach (Func<Type, LayoutManager, ILayout> provider in _layoutProviders)
                    {
                        layout = provider(t, this);
                        if (layout != null)
                        {
                            break;
                        }
                    }
                }
                if (layout == null)
                {
                    throw new LayoutException("Unable to create layout for type " + t.FullName);
                }
                _layouts.Add(t, layout);
            }
            return layout;
        }
    }
}
