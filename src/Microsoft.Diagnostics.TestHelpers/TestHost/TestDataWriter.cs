﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class TestDataWriter
    {
        public readonly XElement Root;
        public readonly XElement Target;

        /// <summary>
        /// Write a test data file from the target
        /// </summary>
        public TestDataWriter()
        {
            Root = new XElement("TestData");
            Root.Add(new XElement("Version", "1.0.2"));
            Target = new XElement("Target");
            Root.Add(Target);
        }

        public void Build(IServiceProvider services)
        {
            ITarget target = services.GetService<ITarget>();
            Debug.Assert(target is not null);
            AddMembers(Target, typeof(ITarget), target, nameof(ITarget.Id), nameof(ITarget.GetTempDirectory));

            XElement modulesElement = new("Modules");
            Target.Add(modulesElement);

            IModuleService moduleService = services.GetService<IModuleService>();
            string runtimeModuleName = target.GetPlatformModuleName("coreclr");
            foreach (IModule module in moduleService.EnumerateModules())
            {
                XElement moduleElement = new("Module");
                modulesElement.Add(moduleElement);
                AddModuleMembers(moduleElement, module, runtimeModuleName);
            }

            XElement threadsElement = new("Threads");
            Target.Add(threadsElement);

            IThreadService threadService = services.GetService<IThreadService>();
            int[] registerIndexes = new int[] { threadService.InstructionPointerIndex, threadService.StackPointerIndex, threadService.FramePointerIndex };
            foreach (IThread thread in threadService.EnumerateThreads())
            {
                XElement threadElement = new("Thread");
                threadsElement.Add(threadElement);
                AddMembers(threadElement, typeof(IThread), thread, nameof(IThread.ThreadIndex), nameof(IThread.GetThreadContext));

                XElement registersElement = new("Registers");
                threadElement.Add(registersElement);
                foreach (int registerIndex in registerIndexes)
                {
                    XElement registerElement = new("Register");
                    registersElement.Add(registerElement);

                    if (threadService.TryGetRegisterInfo(registerIndex, out RegisterInfo info))
                    {
                        AddMembers(registerElement, typeof(RegisterInfo), info, nameof(object.ToString), nameof(object.GetHashCode));
                    }
                    if (thread.TryGetRegisterValue(registerIndex, out ulong value))
                    {
                        registerElement.Add(new XElement("Value", $"0x{value:X16}"));
                    }
                }
            }

            XElement runtimesElement = new("Runtimes");
            Target.Add(runtimesElement);

            IRuntimeService runtimeService = services.GetService<IRuntimeService>();
            foreach (IRuntime runtime in runtimeService.EnumerateRuntimes())
            {
                XElement runtimeElement = new("Runtime");
                runtimesElement.Add(runtimeElement);
                AddMembers(runtimeElement, typeof(IRuntime), runtime, nameof(IRuntime.GetDacFilePath), nameof(IRuntime.GetDbiFilePath));

                XElement runtimeModuleElement = new("RuntimeModule");
                runtimeElement.Add(runtimeModuleElement);
                AddModuleMembers(runtimeModuleElement, runtime.RuntimeModule, symbolModuleName: null);
            }
        }

        public void Write(string testDataFile)
        {
            File.WriteAllText(testDataFile, Root.ToString());
        }

        private void AddModuleMembers(XElement element, IModule module, string symbolModuleName)
        {
            AddMembers(element, typeof(IModule), module,
                nameof(IModule.ModuleIndex),
                nameof(IModule.GetPdbFileInfos),
                nameof(IModule.GetVersionString),
                nameof(IModule.GetSymbolFileName),
                nameof(IModule.LoadSymbols));

            if (symbolModuleName != null && IsModuleEqual(module, symbolModuleName))
            {
                IExportSymbols exportSymbols = module.Services.GetService<IExportSymbols>();
                if (exportSymbols is not null)
                {
                    XElement exportSymbolsElement = null;

                    string symbol1 = "coreclr_initialize";
                    if (exportSymbols.TryGetSymbolAddress(symbol1, out ulong offset1))
                    {
                        XElement symbolElement = AddExportSymbolSection();
                        symbolElement.Add(new XElement("Name", symbol1));
                        symbolElement.Add(new XElement("Value", ToHex(offset1)));
                    }
                    string symbol2 = "coreclr_execute_assembly";
                    if (exportSymbols.TryGetSymbolAddress(symbol2, out ulong offset2))
                    {
                        XElement symbolElement = AddExportSymbolSection();
                        symbolElement.Add(new XElement("Name", symbol2));
                        symbolElement.Add(new XElement("Value", ToHex(offset2)));
                    }

                    XElement AddExportSymbolSection()
                    {
                        if (exportSymbolsElement == null)
                        {
                            exportSymbolsElement = new XElement("ExportSymbols");
                            element.Add(exportSymbolsElement);
                        }
                        XElement symbolElement = new("Symbol");
                        exportSymbolsElement.Add(symbolElement);
                        return symbolElement;
                    }
                }

                IModuleSymbols moduleSymbols = module.Services.GetService<IModuleSymbols>();
                if (moduleSymbols is not null)
                {
                    XElement symbolsElement = null;

                    string symbol1 = "coreclr_initialize";
                    if (moduleSymbols.TryGetSymbolAddress(symbol1, out ulong offset1))
                    {
                        XElement symbolElement = AddExportSymbolSection();
                        symbolElement.Add(new XElement("Name", symbol1));
                        symbolElement.Add(new XElement("Value", ToHex(offset1)));
                        symbolElement.Add(new XElement("Displacement", "0"));
                    }

                    XElement AddExportSymbolSection()
                    {
                        if (symbolsElement == null)
                        {
                            symbolsElement = new XElement("Symbols");
                            element.Add(symbolsElement);
                        }
                        XElement symbolElement = new("Symbol");
                        symbolsElement.Add(symbolElement);
                        return symbolElement;
                    }
                }
            }
        }

        private static void AddMembers(XElement element, Type type, object instance, params string[] membersToSkip)
        {
            MemberInfo[] members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance);
            foreach (MemberInfo member in members)
            {
                if (membersToSkip.Any((skip) => member.Name == skip))
                {
                    continue;
                }
                string result = null;
                object memberValue = null;
                Type memberType = null;

                switch (member.MemberType)
                {
                    case MemberTypes.Property:
                        memberValue = ((PropertyInfo)member).GetValue(instance);
                        memberType = ((PropertyInfo)member).PropertyType;
                        break;
                    case MemberTypes.Field:
                        memberValue = ((FieldInfo)member).GetValue(instance);
                        memberType = ((FieldInfo)member).FieldType;
                        break;
                    case MemberTypes.Method:
                        MethodInfo methodInfo = (MethodInfo)member;
                        if (!methodInfo.IsSpecialName && methodInfo.GetParameters().Length == 0 && methodInfo.ReturnType != typeof(void))
                        {
                            memberValue = ((MethodInfo)member).Invoke(instance, null);
                            memberType = ((MethodInfo)member).ReturnType;
                        }
                        break;
                }
                if (memberType != null)
                {
                    Type nullableType = Nullable.GetUnderlyingType(memberType);
                    memberType = nullableType ?? memberType;

                    if (nullableType != null && memberValue == null)
                    {
                        result = "";
                    }
                    else if (memberType == typeof(string))
                    {
                        result = (string)memberValue ?? "";
                    }
                    else if (memberType == typeof(bool))
                    {
                        result = (bool)memberValue ? "true" : "false";
                    }
                    else if (memberValue is ImmutableArray<byte> buildId)
                    {
                        if (!buildId.IsDefaultOrEmpty)
                        {
                            result = string.Concat(buildId.Select((b) => b.ToString("x2")));
                        }
                    }
                    else if (memberType.IsEnum)
                    {
                        result = memberValue.ToString();
                    }
                    else if (memberType.IsPrimitive)
                    {
                        if (memberType == typeof(short) || memberType == typeof(int) || memberType == typeof(long))
                        {
                            result = memberValue.ToString();
                        }
                        else
                        {
                            int digits = Marshal.SizeOf(memberType) * 2;
                            result = string.Format($"0x{{0:X{digits}}}", memberValue);
                        }
                    }
                    else if (memberType.IsValueType || memberType == typeof(Version) || memberType == typeof(PdbFileInfo))
                    {
                        result = memberValue?.ToString();
                    }
                }
                if (result != null)
                {
                    element.Add(new XElement(member.Name, result));
                }
            }
        }

        private static string ToHex<T>(T value) where T : struct
        {
            int digits = Marshal.SizeOf(typeof(T)) * 2;
            return string.Format($"0x{{0:X{digits}}}", value);
        }

        private static bool IsModuleEqual(IModule module, string moduleName)
        {
            if (module.Target.OperatingSystem == OSPlatform.Windows)
            {
                return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(module.FileName), moduleName);
            }
            else
            {
                return string.Equals(Path.GetFileName(module.FileName), moduleName);
            }
        }
    }
}
