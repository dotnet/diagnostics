// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace CLRNotificationHandlerFailure
{
    internal static class Program
    {
        private static void Main()
        {
            TriggerNotification();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void TriggerNotification()
        {
            for (int index = 0; index < 4; index++)
            {
                AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName($"CLRNotificationHandlerFailure.Dynamic{index}"),
                    AssemblyBuilderAccess.Run);
                ModuleBuilder module = assembly.DefineDynamicModule($"Dynamic{index}");
                TypeBuilder type = module.DefineType($"DynamicType{index}");
                type.CreateType();
            }
            Debugger.Break();
        }
    }
}
