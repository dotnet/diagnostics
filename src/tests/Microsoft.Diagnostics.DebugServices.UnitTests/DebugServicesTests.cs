using Microsoft.Diagnostics.Repl;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.TestHelpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class DebugServicesTests : IDisposable
    {
        private const string ListenerName = "DebugServicesTests";

        private static readonly string[] s_excludedModules = new string[] { "MpClient.dll", "MpOAV.dll" };

        private static IEnumerable<object[]> _configurations;

        public static IEnumerable<object[]> GetConfigurations()
        {
            _configurations ??= TestRunConfiguration.Instance.Configurations
                .Where((config) => config.AllSettings.ContainsKey("DumpFile"))
                .Select((config) => TestHost.CreateHost(config))
                .Select((host) => new[] { host }).ToImmutableArray();
            return _configurations;
        }

        ITestOutputHelper Output { get; set; }

        public DebugServicesTests(ITestOutputHelper output)
        {
            Output = output;

            if (Trace.Listeners[ListenerName] == null) 
            {
                Trace.Listeners.Add(new LoggingListener(output));
                Trace.AutoFlush = true;
            }
        }

        void IDisposable.Dispose() => Trace.Listeners.Remove(ListenerName);

        [SkippableTheory, MemberData(nameof(GetConfigurations))]
        public void TargetTests(TestHost host)
        {
            ITarget target = host.Target;
            Assert.NotNull(target);

            var contextService = target.Services.GetService<IContextService>();
            Assert.NotNull(contextService);
            Assert.NotNull(contextService.GetCurrentTarget());

            // Check that the ITarget properties match the test data
            host.TestData.Target.CompareMembers(target);

            // Test temp directory
            AssertX.DirectoryExists("Target temporary directory", target.GetTempDirectory(), Output);
        }

        [SkippableTheory, MemberData(nameof(GetConfigurations))]
        public void ModuleTests(TestHost host)
        {
            var moduleService = host.Target.Services.GetService<IModuleService>();
            Assert.NotNull(moduleService);

            foreach (ImmutableDictionary<string, TestDataReader.Value> moduleData in host.TestData.Modules)
            {
                if (moduleData.TryGetValue("FileName", out string moduleFileName))
                {
                    if (s_excludedModules.Contains(Path.GetFileName(moduleFileName)))
                    {
                        continue;
                    }
                }
                // Test the module service's GetModuleFromBaseAddress() and GetModuleFromAddress()
                Assert.True(moduleData.TryGetValue("ImageBase", out ulong imageBase));

                IModule module = null;
                try
                {
                    module = moduleService.GetModuleFromBaseAddress(imageBase);
                }
                catch (DiagnosticsException)
                {
                    Trace.TraceInformation($"GetModuleFromBaseAddress({imageBase:X16}) {moduleFileName} FAILED");

                    // Skip modules not found when running under lldb
                    if (host.Target.Host.HostType == HostType.Lldb) {
                        continue;
                    }
                }
                Assert.NotNull(module);

                if (host.Target.Host.HostType != HostType.Lldb)
                { 
                    // Check that the resulting module matches the test data
                    moduleData.CompareMembers(module);
                }

                IModule module1 = moduleService.GetModuleFromIndex(module.ModuleIndex);
                Assert.NotNull(module1);
                Assert.Equal(module, module1);

                // Test GetModuleFromAddress on various address in module
                IModule module2 = moduleService.GetModuleFromAddress(imageBase);
                Assert.NotNull(module2);
                Assert.True(module.ModuleIndex == module2.ModuleIndex);
                Assert.Equal(module, module2);

                module2 = moduleService.GetModuleFromAddress(imageBase + 0x100);
                Assert.NotNull(module2);
                Assert.True(module.ModuleIndex == module2.ModuleIndex);
                Assert.Equal(module, module2);

                module2 = moduleService.GetModuleFromAddress(imageBase + module.ImageSize - 1);
                Assert.NotNull(module2);
                Assert.True(module.ModuleIndex == module2.ModuleIndex);
                Assert.Equal(module, module2);

                // Find this module in the list of all modules
                Assert.NotNull(moduleService.EnumerateModules().SingleOrDefault((mod) => mod.ImageBase == imageBase));

                if (host.Target.Host.HostType != HostType.Lldb)
                {
                    // Test the module service's GetModuleFromName()
                    if (!string.IsNullOrEmpty(moduleFileName))
                    {
                        IEnumerable<IModule> modules = moduleService.GetModuleFromModuleName(moduleFileName);
                        Assert.NotNull(modules);
                        Assert.True(modules.Any());

                        foreach (IModule mod in modules)
                        {
                            if (mod.ImageBase == imageBase)
                            {
                                // Check that the resulting module matches the test data
                                moduleData.CompareMembers(mod);
                            }
                        }
                    }
                }

                // Test the symbol lookup module interfaces
                if (host.Target.Host.HostType != HostType.DotnetDump)
                {
                    if (moduleData.TryGetValue("ExportSymbols", out TestDataReader.Value testExportSymbols))
                    {
                        IExportSymbols exportSymbols = module.Services.GetService<IExportSymbols>();
                        Assert.NotNull(exportSymbols);

                        foreach (ImmutableDictionary<string, TestDataReader.Value> symbol in testExportSymbols.Values)
                        {
                            Assert.True(symbol.TryGetValue("Name", out string name));
                            Assert.True(symbol.TryGetValue("Value", out ulong value));
                            Trace.TraceInformation("IExportSymbols.GetSymbolAddress({0}) == {1:X16}", name, value);
                            
                            Assert.True(exportSymbols.TryGetSymbolAddress(name, out ulong offset));
                            Assert.Equal(value, offset);
                        }
                    }

                    if (moduleData.TryGetValue("Symbols", out TestDataReader.Value testSymbols))
                    {
                        IModuleSymbols moduleSymbols = module.Services.GetService<IModuleSymbols>();
                        Assert.NotNull(moduleSymbols);

                        foreach (ImmutableDictionary<string, TestDataReader.Value> symbol in testSymbols.Values)
                        {
                            Assert.True(symbol.TryGetValue("Name", out string symbolName));
                            Assert.True(symbol.TryGetValue("Value", out ulong symbolValue));
                            Trace.TraceInformation("IModuleSymbols.GetSymbolAddress({0}) == {1:X16}", symbolName, symbolValue);

                            Assert.True(moduleSymbols.TryGetSymbolName(symbolValue, out string name, out ulong displacement));
                            Assert.Equal(symbolName, name);

                            Assert.True(moduleSymbols.TryGetSymbolAddress(symbolName, out ulong value));
                            Assert.Equal(symbolValue, value);

                            if (symbol.TryGetValue("Displacement", out ulong symbolDisplacement))
                            {
                                Assert.Equal(symbolDisplacement, displacement);
                            }
                        }
                    }
                }
            }
        }

        [SkippableTheory, MemberData(nameof(GetConfigurations))]
        public void ThreadTests(TestHost host)
        {
            var threadService = host.Target.Services.GetService<IThreadService>();
            Assert.NotNull(threadService);

            foreach (ImmutableDictionary<string, TestDataReader.Value> threadData in host.TestData.Threads)
            {
                Assert.True(threadData.TryGetValue("ThreadId", out uint threadId));
                
                IThread thread = threadService.GetThreadFromId(threadId);
                Assert.NotNull(thread);

                // Check that the resulting thread matches the test data
                threadData.CompareMembers(thread);

                IThread thread2 = threadService.GetThreadFromIndex(thread.ThreadIndex);
                Assert.NotNull(thread2);
                Assert.Equal(thread, thread2);

                // Check the registers in the test data
                ImmutableArray<ImmutableDictionary<string, TestDataReader.Value>> registers = threadData["Registers"].Values;
                Assert.True(registers.Length > 0);

                foreach (ImmutableDictionary<string, TestDataReader.Value> register in registers)
                {
                    Assert.True(register.TryGetValue("RegisterIndex", out int registerIndex));

                    Assert.True(threadService.TryGetRegisterInfo(registerIndex, out RegisterInfo registerInfo));
                    register.CompareMembers(registerInfo);

                    Assert.True(thread.TryGetRegisterValue(registerIndex, out ulong value));
                    Assert.Equal(value, register["Value"].GetValue<ulong>());

                    Assert.True(threadService.TryGetRegisterIndexByName(registerInfo.RegisterName, out int ri));
                    Assert.Equal(ri, registerIndex);
                }

                // Find this thread on the list of all threads
                Assert.NotNull(threadService.EnumerateThreads().SingleOrDefault((th) => th.ThreadId == threadId));
            }
        }

        [SkippableTheory, MemberData(nameof(GetConfigurations))]
        public void RuntimeTests(TestHost host)
        {
            // The current Linux test assets are not alpine/musl
            if (OS.IsAlpine)
            {
                throw new SkipTestException("Not supported on Alpine Linux");
            }
            var runtimeService = host.Target.Services.GetService<IRuntimeService>();
            Assert.NotNull(runtimeService);

            var contextService = host.Target.Services.GetService<IContextService>();
            Assert.NotNull(contextService);
            Assert.NotNull(contextService.GetCurrentRuntime());

            foreach (ImmutableDictionary<string, TestDataReader.Value> runtimeData in host.TestData.Runtimes)
            {
                if (runtimeData.TryGetValue("Id", out int id))
                {
                    IRuntime runtime = runtimeService.EnumerateRuntimes().FirstOrDefault((r) => r.Id == id);
                    Assert.NotNull(runtime);

                    runtimeData.CompareMembers(runtime);

                    ClrInfo clrInfo = runtime.Services.GetService<ClrInfo>();
                    Assert.NotNull(clrInfo);

                    ClrRuntime clrRuntime = runtime.Services.GetService<ClrRuntime>();
                    Assert.NotNull(clrRuntime);
                    Assert.NotEmpty(clrRuntime.AppDomains);
                    Assert.NotEmpty(clrRuntime.Threads);
                    Assert.NotEmpty(clrRuntime.EnumerateModules());
                    if (!host.DumpFile.Contains("Triage"))
                    {
                        Assert.NotEmpty(clrRuntime.EnumerateHandles());
                    }
                }
            }
        }

        class LoggingListener : TraceListener
        {
            private readonly CharToLineConverter _converter;

            internal LoggingListener(ITestOutputHelper output)
                : base(ListenerName)
            {
                _converter = new CharToLineConverter((text) => {
                    output.WriteLine(text);
                });
            }

            public override void Write(string message)
            {
                _converter.Input(message);
            }

            public override void WriteLine(string message)
            {
                _converter.Input(message + Environment.NewLine);
            }
        }
    }
}
