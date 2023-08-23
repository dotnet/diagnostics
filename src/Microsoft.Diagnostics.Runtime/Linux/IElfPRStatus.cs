// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// An abstraction of the ELF PRStatus view.
    /// </summary>
    internal interface IElfPRStatus
    {
        /// <summary>
        /// The process id associated with this prstatus
        /// </summary>
        uint ProcessId { get; }

        /// <summary>
        /// The thread id of this prstatus.
        /// </summary>
        uint ThreadId { get; }

        /// <summary>
        /// Copies the registers within this prstatus into the Windows _CONTEXT structure for the specified
        /// architecture.
        ///
        /// <see cref="Arm64Context"/>
        /// <see cref="AMD64Context"/>
        /// <see cref="ArmContext"/>
        /// <see cref="X86Context"/>
        /// </summary>
        /// <param name="context">A span to copy the context into.  This should generally be one of the predefined *Context structs,
        /// e.g. <see cref="AMD64Context"/>.</param>
        /// <returns>True if the registers were copied to the context, false otherwise.  Usually a return value of false means that
        /// <paramref name="context"/> was too small.</returns>
        bool CopyRegistersAsContext(Span<byte> context);
    }
}