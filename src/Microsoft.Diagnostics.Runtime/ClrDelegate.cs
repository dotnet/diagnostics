// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents a delegate instance in the target process.
    /// </summary>
    public struct ClrDelegate : IClrDelegate
    {
        internal const string DelegateType = "System.Delegate";
        /// <summary>
        /// Constructs a <see cref="ClrDelegate"/> from a <see cref="ClrObject"/>.  Note that obj.IsDelegate
        /// must be true.
        /// </summary>
        /// <param name="obj">A delegate object</param>
        internal ClrDelegate(ClrObject obj)
        {
            DebugOnly.Assert(obj.IsDelegate);

            Object = obj;
        }

        /// <summary>
        /// Returns whether this delegate has multiple targets or not.  If this method returns true then it is expected
        /// that <see cref="GetDelegateTarget"/> will return <see langword="null"/> and you should use
        /// <see cref="EnumerateDelegateTargets"/> instead.
        /// </summary>
        public bool HasMultipleTargets
        {
            get
            {
                if (Object.Type is null)
                    return false;


                return Object.TryReadField("_invocationCount", out int count) && count > 0;
            }
        }

        /// <summary>
        /// The actual object represented by this ClrDelegate instance.
        /// </summary>
        public ClrObject Object { get; }

        IClrValue IClrDelegate.Object => Object;

        /// <summary>
        /// Returns a the single delegate target of the
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">If this object is not a delegate we throw</exception>
        public ClrDelegateTarget? GetDelegateTarget()
        {
            if (Object.Type is null)
                throw new InvalidOperationException($"Object {this} is not a delegate.");

            if (!Object.TryReadObjectField("_target", out ClrObject target))
                return null;

            bool seenOne = false;
            ClrRuntime runtime = Object.Type.Heap.Runtime;

            IEnumerable<ClrInstanceField> methodPointerFields = Object.Type.Fields.Where(f => f.Name != null).Where(f => f.Name!.Equals("_methodPtr") || f.Name.Equals("_methodPtrAux"));
            foreach (ClrInstanceField field in methodPointerFields)
            {
                if (field.ElementType == ClrElementType.NativeInt)
                {
                    ulong targetMethod = field.Read<UIntPtr>(Object, interior: false).ToUInt64();

                    if (targetMethod != 0)
                    {
                        ClrMethod? result = runtime.GetMethodByInstructionPointer(targetMethod);
                        if (result is not null)
                            return new ClrDelegateTarget(this, target, result);
                    }

                    seenOne = true;
                }
            }

            // If we didn't see at least one "_methodPtr*" field then actually check if this is a delegate and throw appropriately.
            if (!seenOne && !Validate())
                throw new InvalidOperationException($"Object {this} is not a delegate.");

            return null;
        }

        private bool Validate()
        {
            // The overall goal here is to throw an exception when the user created a ClrDelegate class on an object that isn't
            // a delegate.  However, we DON'T want to throw when we are failing to find the right fields due to missing metadata.
            // Note it's ok to be slow in this method because we are down a failure path that should usually not happen in practice.

            if (Object.Type is null)
                return false;

            // If we have no fields then this isn't a delegate.
            if (Object.Type.Fields.Length == 0)
                return false;

            // Assume this is a valid
            bool seenAny = false;
            bool allNull = true;

            foreach (ClrInstanceField field in Object.Type.Fields)
            {
                seenAny |= field.Name is "_methodPtr" or "_methodPtrAux";
                allNull &= field.Name is null;
            }

            // If all field names were null then we cannot validate whether this was a delegate or not.  The case we are worried
            // about here is if we have no
            if (allNull)
                return true;

            // If we saw fields we expected return it's valid even if we didn't understand this delegate.
            if (seenAny)
                return true;

            ClrType? curr = Object.Type;

            for (int i = 0; i < 8 && curr != null; i++, curr = curr.BaseType)
            {
                // If we found System.Delegate, we are done.
                if (curr.Name == DelegateType)
                    return true;

                // If we found a blank name in mscorlib then we have a metadata problem, and we cannot validate this delegate.
                // Don't throw an exception in this case.
                if (curr.Name == null && curr.Module == Object.Type.Heap.Runtime.BaseClassLibrary)
                    return true;
            }

            // We are definitely not a delegate.
            return false;
        }

        /// <summary>
        /// Enumerates all delegate targets of this delegate.  If called on a MulitcastDelegate, this will enumerate all
        /// targets that will be called when this delegate is invoked.  If called on a non-MulticastDelegate, this will
        /// enumerate the value of GetDelegateTarget.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ClrDelegateTarget> EnumerateDelegateTargets()
        {
            ClrDelegateTarget? first = GetDelegateTarget();
            if (first != null)
                yield return first;

            // The call to GetDelegateMethod will validate that we are a valid object and a subclass of System.Delegate
            if (!Object.TryReadField("_invocationCount", out int count)
                || count == 0
                || !Object.TryReadObjectField("_invocationList", out ClrObject invocationList)
                || !invocationList.IsArray)
            {
                yield break;
            }

            ClrArray invocationArray = invocationList.AsArray();
            count = Math.Min(count, invocationArray.Length);

            ClrHeap heap = Object.Type!.Heap;

            UIntPtr[]? pointers = invocationArray.ReadValues<UIntPtr>(0, count);
            if (pointers is not null)
            {
                foreach (UIntPtr ptr in pointers)
                {
                    if (ptr == UIntPtr.Zero)
                        continue;

                    ClrObject delegateObj = heap.GetObject(ptr.ToUInt64());
                    if (delegateObj.IsDelegate)
                    {
                        ClrDelegateTarget? delegateTarget = new ClrDelegate(delegateObj).GetDelegateTarget();
                        if (delegateTarget is not null)
                            yield return delegateTarget;
                    }
                }
            }
        }

        IEnumerable<IClrDelegateTarget> IClrDelegate.EnumerateDelegateTargets() => EnumerateDelegateTargets().Cast<IClrDelegateTarget>();

        IClrDelegateTarget? IClrDelegate.GetDelegateTarget() => GetDelegateTarget();
    }
}