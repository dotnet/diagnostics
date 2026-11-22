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
        private Dictionary<Tuple<Type, uint>, ILayout> _legacyArrayLayouts = new();
        private Dictionary<Tuple<Type, uint>, ILayout> _typedArrayLayouts = new();
        private readonly HashSet<Type> _registeredLayoutFactoryTypes = new();
        private readonly Dictionary<Type, Func<LayoutManager, uint, ILayout>> _arrayFactories = new();

        public LayoutManager() { }

        public void AddLayout(ILayout layout)
        {
            // Explicit layouts are resolved before registered factories and providers.
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
            if (!_legacyArrayLayouts.TryGetValue(key, out layout))
            {
                Type elemType = arrayType.GetElementType();
                layout = new ArrayLayout(arrayType, GetLayout(elemType), numElements);
                _legacyArrayLayouts.Add(key, layout);
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
            if (!_typedArrayLayouts.TryGetValue(key, out ILayout layout))
            {
                layout = CreateArrayLayout<T>(numElements);
                _typedArrayLayouts.Add(key, layout);
            }
            return layout;
        }

        /// <summary>Registers typed allocation for fixed array fields whose elements have a layout.</summary>
        public LayoutManager RegisterArray<T>()
        {
            Type arrayType = typeof(T[]);
            if (!_arrayFactories.ContainsKey(arrayType))
            {
                _arrayFactories.Add(arrayType, (manager, count) => manager.CreateArrayLayout<T>(count));
            }
            return this;
        }

        private ILayout CreateArrayLayout<T>(uint count)
        {
            return new ArrayLayout<T>(GetLayout<T>(), count);
        }

        internal ILayout GetRegisteredArrayLayout(Type arrayType, uint count)
        {
            Tuple<Type, uint> key = new(arrayType, count);
            if (!_typedArrayLayouts.TryGetValue(key, out ILayout layout))
            {
                if (!_arrayFactories.TryGetValue(arrayType, out Func<LayoutManager, uint, ILayout> factory))
                {
                    throw new LayoutException("No array allocator registered for " + arrayType.FullName + ". Use RegisterArray<T>.");
                }
                layout = factory(this, count);
                _typedArrayLayouts.Add(key, layout);
            }
            return layout;
        }

        internal void RegisterLayoutFactory(Type type, Func<LayoutManager, ILayout> factory)
        {
            if (!_registeredLayoutFactoryTypes.Add(type))
            {
                throw new ArgumentException("A layout factory is already registered for " + type.FullName, nameof(type));
            }
            // Factories participate in the same registration order as custom providers.
            _layoutProviders.Add((requestedType, manager) => requestedType == type ? factory(manager) : null);
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
                foreach (Func<Type, LayoutManager, ILayout> provider in _layoutProviders)
                {
                    layout = provider(t, this);
                    if (layout != null)
                    {
                        break;
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
