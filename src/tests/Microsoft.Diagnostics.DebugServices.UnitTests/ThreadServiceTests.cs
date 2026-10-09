// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class ThreadServiceTests
    {
        [Theory]
        [InlineData(Architecture.X64)]
        [InlineData(Architecture.X86)]
        [InlineData(Architecture.Arm64)]
        [InlineData(Architecture.Arm)]
        [InlineData((Architecture)6)]
        [InlineData((Architecture)9)]
        public void RegisterMetadataMatchesContextLayout(Architecture architecture)
        {
            TestTarget target = new(architecture);
            using TestThreadService service = new(target.Services);
            Type contextType = architecture switch
            {
                Architecture.X64 => typeof(AMD64Context),
                Architecture.X86 => typeof(X86Context),
                Architecture.Arm64 => typeof(Arm64Context),
                Architecture.Arm => typeof(ArmContext),
                (Architecture)6 => typeof(LoongArch64Context),
                (Architecture)9 => typeof(RiscV64Context),
                _ => throw new NotSupportedException()
            };
            FieldInfo[] fields = contextType.GetFields(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => field.GetCustomAttribute<RegisterAttribute>() is RegisterAttribute attribute &&
                    (attribute.RegisterType & RegisterType.TypeMask) is RegisterType.Control or RegisterType.General or RegisterType.Segments)
                .ToArray();
            RegisterInfo[] registers = service.Registers.ToArray();
            Assert.NotEmpty(registers);
            Assert.Equal(fields.Length, registers.Length);

            for (int index = 0; index < fields.Length; index++)
            {
                FieldInfo field = fields[index];
                RegisterAttribute attribute = field.GetCustomAttribute<RegisterAttribute>();
                RegisterInfo actual = registers[index];
                Assert.Equal(index, actual.RegisterIndex);
                Assert.Equal(field.GetCustomAttribute<FieldOffsetAttribute>().Value, actual.RegisterOffset);
                Assert.Equal(Marshal.SizeOf(field.FieldType), actual.RegisterSize);
                Assert.Equal(attribute.Name ?? field.Name.ToLowerInvariant(), actual.RegisterName);
                Assert.True(service.TryGetRegisterIndexByName(actual.RegisterName, out int registerIndex));
                Assert.Equal(index, registerIndex);
                Assert.True(service.TryGetRegisterInfo(index, out RegisterInfo lookup));
                Assert.Equal(actual, lookup);

                if ((attribute.RegisterType & RegisterType.ProgramCounter) != 0)
                    Assert.Equal(index, service.InstructionPointerIndex);
                if ((attribute.RegisterType & RegisterType.StackPointer) != 0)
                    Assert.Equal(index, service.StackPointerIndex);
                if ((attribute.RegisterType & RegisterType.FramePointer) != 0)
                    Assert.Equal(index, service.FramePointerIndex);
            }
        }

        private sealed class TestThreadService : ThreadService
        {
            public TestThreadService(IServiceProvider services) : base(services) { }
            protected override IEnumerable<IThread> GetThreadsInner() => [];
        }

        private sealed class TestTarget : ITarget
        {
            public TestTarget(Architecture architecture)
            {
                Architecture = architecture;
                ServiceContainer services = new(null);
                services.AddService<ITarget>(this);
                Services = services;
            }

            public IHost Host { get; } = new TestHost();
            public int Id => 0;
            public OSPlatform OperatingSystem => OSPlatform.Windows;
            public Architecture Architecture { get; }
            public bool IsDump => true;
            public uint? ProcessId => null;
            public IServiceProvider Services { get; }
            public IServiceEvent OnFlushEvent { get; } = new ServiceEvent();
            public IServiceEvent OnDestroyEvent { get; } = new ServiceEvent();
            public void Flush() => OnFlushEvent.Fire();
            public void Destroy() => OnDestroyEvent.Fire();
        }

        private sealed class TestHost : IHost
        {
            public HostType HostType => HostType.DotnetDump;
            public IServiceEvent OnShutdownEvent => throw new InvalidOperationException();
            public IServiceEvent<ITarget> OnTargetCreate => throw new InvalidOperationException();
            public IServiceProvider Services => throw new InvalidOperationException();
            public IEnumerable<ITarget> EnumerateTargets() => throw new InvalidOperationException();
            public int AddTarget(ITarget target) => throw new InvalidOperationException();
            public string GetTempDirectory() => throw new InvalidOperationException();
        }
    }
}
