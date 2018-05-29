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
            string dllPath = args[0];
            Console.WriteLine("SymbolTestApp starting {0}", dllPath);
            Foo1(42, dllPath);
        }

        static int Foo1(int x, string dllPath)
        {
            return Foo2(x, dllPath);
        }

        static int Foo2(int x, string dllPath)
        {
            Foo4(dllPath);
            return x;
        }

        static void Foo4(string dllPath)
        {
#if FULL_CLR
            byte[] dll = File.ReadAllBytes(Path.Combine(dllPath, @"symboltestdll.dll"));
            byte[] pdb = File.ReadAllBytes(Path.Combine(dllPath, @"symboltestdll.pdb"));
            Assembly assembly = Assembly.Load(dll, pdb);
#else
            Stream dll = File.OpenRead(Path.Combine(dllPath, @"symboltestdll.dll"));
            Stream pdb = File.OpenRead(Path.Combine(dllPath, @"symboltestdll.pdb"));
            Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(dll, pdb);
#endif
            Type dllType = assembly.GetType("SymbolTestDll.TestClass");
            MethodInfo dllMethod = dllType.GetMethod("ThrowException");
            dllMethod.Invoke(null, null);
        }
    }
}
