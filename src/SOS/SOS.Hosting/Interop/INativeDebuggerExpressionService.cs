// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SOS.Hosting.Interop
{
    [GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf8)]
    [Guid("b8e9e3d1-0247-4ea6-8aa1-2dfda28d155a")]
    public partial interface INativeDebuggerExpressionService
    {
        [PreserveSig]
        int EvaluateExpression(string expression, out ulong value);
    }
}
