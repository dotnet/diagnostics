// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
#if !FULL_CLR
using System.Runtime.Loader;
#endif
using System.Threading.Tasks;

namespace SymbolTestApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            s_pipeName = args.Length > 1 ? args[0] : null;
            string dllPath = args.Length > 1 ? args[1] : args.Length > 0 ? args[0] : string.Empty;
            Console.WriteLine("SymbolTestApp starting {0}", dllPath);
            Foo1(42, dllPath);
        }

        private static int Foo1(int x, string dllPath)
        {
            return Foo2(x, dllPath);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int Foo2(int x, string dllPath)
        {
            Foo4(dllPath);
            return x;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Foo4(string dllPath)
        {
#if FULL_CLR
            byte[] dll = File.ReadAllBytes(Path.Combine(dllPath, @"SymbolTestDll.dll"));
            byte[] pdb = null;
            string pdbFile = Path.Combine(dllPath, @"SymbolTestDll.pdb");
            if (File.Exists(pdbFile)) {
                pdb = File.ReadAllBytes(pdbFile);
            }
            Assembly assembly = Assembly.Load(dll, pdb);
#else
            Stream dll = File.OpenRead(Path.Combine(dllPath, @"SymbolTestDll.dll"));
            Stream pdb = null;
            string pdbFile = Path.Combine(dllPath, @"SymbolTestDll.pdb");
            if (File.Exists(pdbFile)) {
                pdb = File.OpenRead(pdbFile);
            }
            Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(dll, pdb);
#endif
            Type dllType = assembly.GetType("SymbolTestDll.TestClass");
            MethodInfo dllMethod = dllType.GetMethod("ThrowException");
            if (s_pipeName != null)
            {
                WaitForDump();
            }
            dllMethod.Invoke(null, new object[] { "This is the exception message" });
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void WaitForDump()
        {
            using (System.IO.Pipes.NamedPipeClientStream pipeStream = new System.IO.Pipes.NamedPipeClientStream(s_pipeName))
            {
                pipeStream.Connect();
                pipeStream.ReadByte();
            }
        }

        private static string s_pipeName;
    }
}
