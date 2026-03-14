using System;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace TestInfiniteDynamicMethods
{
    internal class Program
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static Func<int, int> GetFibDynamicMethod()
        {
            DynamicMethod dynamicMethod = new DynamicMethod("Fibonacci", typeof(int), new Type[] { typeof(int) });
            ILGenerator ilgen = dynamicMethod.GetILGenerator();
            Label labelAfterCmp0 = ilgen.DefineLabel();
            ilgen.Emit(OpCodes.Ldarg_0);
            ilgen.Emit(OpCodes.Ldc_I4_0);
            ilgen.Emit(OpCodes.Bne_Un_S, labelAfterCmp0);
            ilgen.Emit(OpCodes.Ldc_I4_0);
            ilgen.Emit(OpCodes.Ret);
            ilgen.MarkLabel(labelAfterCmp0);

            Label labelAfterCmp1 = ilgen.DefineLabel();
            ilgen.Emit(OpCodes.Ldarg_0);
            ilgen.Emit(OpCodes.Ldc_I4_1);
            ilgen.Emit(OpCodes.Bne_Un_S, labelAfterCmp1);
            ilgen.Emit(OpCodes.Ldc_I4_1);
            ilgen.Emit(OpCodes.Ret);
            ilgen.MarkLabel(labelAfterCmp1);

            ilgen.Emit(OpCodes.Ldarg_0);
            ilgen.Emit(OpCodes.Ldc_I4_1);
            ilgen.Emit(OpCodes.Sub);
            ilgen.Emit(OpCodes.Call, dynamicMethod);

            ilgen.Emit(OpCodes.Ldarg_0);
            ilgen.Emit(OpCodes.Ldc_I4_2);
            ilgen.Emit(OpCodes.Sub);
            ilgen.Emit(OpCodes.Call, dynamicMethod);

            ilgen.Emit(OpCodes.Add);
            ilgen.Emit(OpCodes.Ret);

            var result = dynamicMethod.CreateDelegate<Func<int, int>>();
            throw new Exception();
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            int count = 0;
            while (true)
            {
                int result = GetFibDynamicMethod()(4);

                if (((++count) % 200) == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                if (((++count) % 1000) == 0)
                    Console.WriteLine(count);
            }
        }
    }
}
