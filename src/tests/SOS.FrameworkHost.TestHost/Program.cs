// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp;

namespace SOS.FrameworkHost.TestHost
{
    internal static class Program
    {
        private const int InitializationFailure = unchecked((int)0x81234567);

        private static int Main(string[] args)
        {
            try
            {
                if (args[2] == "cleanup")
                {
                    CheckPartialHostCleanup(Path.Combine(Path.GetDirectoryName(args[0]), "SOS.Extensions.dll"));
                    Console.WriteLine("Partial host cleanup scenario passed");
                    return 0;
                }

                string payload = Path.Combine(args[1], "payload");
                Directory.CreateDirectory(payload);
                string bootstrap = Path.Combine(payload, "SOS.FrameworkHost.dll");
                File.Copy(args[0], bootstrap);

                if (args[2] != "missing")
                {
                    string dependency = Path.Combine(payload, "FrameworkHost.TestDependency.dll");
                    Compile(dependency, @"
                        [assembly: System.Reflection.AssemblyVersion(""2.0.0.0"")]
                        public static class Dependency { public static int NewApi() { return 2; } }");
                    Compile(Path.Combine(payload, "SOS.Extensions.dll"), @"
                        using System;
                        namespace SOS.Extensions
                        {
                            public static class HostServices
                            {
                                private static int s_count;
                                public static int Initialize(string argument)
                                {
                                    if (AppDomain.CurrentDomain.IsDefaultAppDomain())
                                        throw new InvalidOperationException(""SOS loaded in the debugger domain"");
                                    if (argument == ""throw"")
                                        throw new InitializationException();
                                    return ++s_count * 100 + int.Parse(argument) + Dependency.NewApi();
                                }
                            }
                            [Serializable]
                            public sealed class InitializationException : Exception
                            {
                                public InitializationException() { HResult = unchecked((int)0x81234567); }
                            }
                        }", dependency);

                    if (args[2] == "conflict")
                    {
                        string parent = Path.Combine(args[1], "parent");
                        Directory.CreateDirectory(parent);
                        string oldDependency = Path.Combine(parent, "FrameworkHost.TestDependency.dll");
                        Compile(oldDependency, @"
                            [assembly: System.Reflection.AssemblyVersion(""1.0.0.0"")]
                            public static class Dependency { public static int OldApi() { return 1; } }");
                        Assembly oldAssembly = Assembly.LoadFrom(oldDependency);
                        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
                            new AssemblyName(request.Name).Name == oldAssembly.GetName().Name ? oldAssembly : null;
                    }
                }

                Assembly host = Assembly.LoadFrom(bootstrap);
                MethodInfo initialize = host.GetType("SOS.FrameworkHost.Host", throwOnError: true)
                    .GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
                if (args[2] == "missing")
                {
                    Check(new FileNotFoundException().HResult, Initialize(initialize, "1"));
                }
                else
                {
                    Check(103, Initialize(initialize, "1"));
                    Check(204, Initialize(initialize, "2"));
                    Check(InitializationFailure, Initialize(initialize, "throw"));
                    Check(305, Initialize(initialize, "3"));
                }

                if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "SOS.Extensions"))
                {
                    throw new InvalidOperationException("SOS payload or exception types leaked into the debugger domain");
                }
                Console.WriteLine("Framework isolation scenario passed: " + args[2]);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static int Initialize(MethodInfo initialize, string argument) =>
            (int)initialize.Invoke(null, new object[] { argument });

        private static void CheckPartialHostCleanup(string path)
        {
            Type hostType = Assembly.LoadFrom(path).GetType("SOS.Extensions.HostServices", throwOnError: true);
            ConstructorInfo constructor = hostType.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(string), typeof(IntPtr) }, null);
            object host = constructor.Invoke(new object[] { path, IntPtr.Zero });
            PropertyInfo instance = hostType.GetProperty("Instance");
            Check(2, (int)hostType.GetMethod("AddRef").Invoke(host, null));
            instance.SetValue(null, host);
            try
            {
                hostType.GetMethod("Uninitialize", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(host, new object[] { IntPtr.Zero });
                if (instance.GetValue(null) != null)
                {
                    throw new InvalidOperationException("Failed initialization left a partial host");
                }
            }
            finally
            {
                instance.SetValue(null, null);
                Check(0, (int)hostType.GetMethod("Release").Invoke(host, null));
            }
        }

        private static void Check(int expected, int actual)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException($"Expected {expected:X8}, actual {actual:X8}");
            }
        }

        private static void Compile(string path, string source, params string[] references)
        {
            using CSharpCodeProvider compiler = new();
            CompilerParameters parameters = new()
            {
                OutputAssembly = path,
                TreatWarningsAsErrors = true
            };
            parameters.ReferencedAssemblies.Add("System.dll");
            parameters.ReferencedAssemblies.AddRange(references);
            CompilerResults result = compiler.CompileAssemblyFromSource(parameters, source);
            if (result.Errors.HasErrors)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine,
                    result.Errors.Cast<CompilerError>().Select(error => error.ToString())));
            }
        }
    }
}
