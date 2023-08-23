// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// The "target" method and object that a delegate points to.
    /// </summary>
    public sealed class ClrDelegateTarget : IClrDelegateTarget
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="del">The parent delgate that this target came from.</param>
        /// <param name="target">The "target" of this delegate.</param>
        /// <param name="method">The method this delegate will call.</param>
        internal ClrDelegateTarget(ClrDelegate del, ClrObject target, ClrMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            Parent = del;
            TargetObject = target;
            Method = method;
        }

        /// <summary>
        /// The parent delegate that this target comes from.
        /// </summary>
        public ClrDelegate Parent { get; }

        /// <summary>
        /// The object that this delegate is targeted to.  If <see cref="Method"/> is an instance method,
        /// this will point to the <see langword="this"/> pointer of that object.  If <see cref="Method"/>
        /// is a static method, this will be a pointer to a delegate.
        /// </summary>
        public ClrObject TargetObject { get; }

        /// <summary>
        /// The method that would be called when <see cref="Parent"/> is invoked in the target process.
        /// </summary>
        public ClrMethod Method { get; }

        IClrMethod IClrDelegateTarget.Method => Method;

        IClrDelegate IClrDelegateTarget.Parent => Parent;

        IClrValue IClrDelegateTarget.TargetObject => TargetObject;
    }
}
