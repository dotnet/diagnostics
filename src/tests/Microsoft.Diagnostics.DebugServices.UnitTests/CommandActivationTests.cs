// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.ExtensionCommands;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class CommandActivationTests
    {
        [Fact]
        public void TypesWithoutCommandAttributesDoNotInvokeTheirFactory()
        {
            CommandService commands = new();

            commands.AddCommands<CommandBase>(_ => throw new InvalidOperationException("Unregistered commands must not be constructed."));

            Assert.Empty(commands.Commands);
        }

        [Fact]
        public void ConstructorRegistrationValidatesPublicConstructorsImmediately()
        {
            CommandService commands = new();

            Assert.Throws<InvalidOperationException>(() => commands.AddCommands<CommandBase>());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ConstructorDependenciesAndCommandBindingArePreserved(bool provideOptionalService)
        {
            ServiceContainer services = CreateServices();
            ActivationState state = new();
            services.AddService(state);
            OptionalDependency optional = provideOptionalService ? new() : null;
            services.AddService(optional);
            CommandService commands = new();
            commands.AddCommands<ActivatedCommand>();

            commands.Execute("activate first --count 7", services);
            commands.Execute("activate second", services);

            Assert.Equal(2, state.Invocations.Count);
            ActivatedCommand first = state.Invocations[0];
            ActivatedCommand second = state.Invocations[1];
            Assert.NotSame(first, second);
            Assert.Same(services, first.Services);
            Assert.Same(services.GetRequiredService<IConsoleService>(), first.Console);
            Assert.Same(state, first.State);
            Assert.Same(optional, first.Optional);
            Assert.Same(optional, second.Optional);
            Assert.Equal("first", first.Value);
            Assert.Equal(7, first.Count);
            Assert.Equal("second", second.Value);
            Assert.Equal(3, second.Count);
        }

        [Fact]
        public void MissingRequiredConstructorDependencyFails()
        {
            CommandService commands = new();
            commands.AddCommands<ActivatedCommand>();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => commands.Execute("activate", CreateServices()));
            Assert.Contains(nameof(ActivationState), exception.Message);
        }

        [Fact]
        public void StaticFilterSkipsUnavailableConstructorDependenciesAndFallsBack()
        {
            ServiceContainer services = CreateServices();
            ActivationState state = new();
            services.AddService(state);
            CommandService commands = new();
            commands.AddCommands<ActivatedCommand>();
            commands.AddCommands<UnavailableCommand>();

            commands.Execute("activate", services);

            Assert.Single(state.Invocations);
            Assert.DoesNotContain(commands.GetAllCommandHelp(services), help => help.Help == "Unavailable command");
        }

        [Fact]
        public void StaticHelpReceivesProviderWithoutConstructingCommand()
        {
            ServiceContainer services = CreateServices();
            CommandService commands = new();
            commands.AddCommands<StaticHelpCommand>();

            Assert.Contains("Static help > ", commands.GetDetailedHelp("static-help", services, int.MaxValue));
        }

        [Fact]
        public void InstanceHelpUsesConstructorDependenciesAndDefaultOptions()
        {
            ServiceContainer services = CreateServices();
            services.AddService(new ActivationState());
            CommandService commands = new();
            commands.AddCommands<ActivatedCommand>();

            Assert.Contains("Count: 3", commands.GetDetailedHelp("activate", services, int.MaxValue));
        }

        [Fact]
        public void CustomCommandFactoryReceivesCurrentProvider()
        {
            ServiceContainer services = CreateServices();
            int invocations = 0;
            CommandService implementation = new();
            ICommandService commands = implementation;
            commands.AddCommands<CallbackCommand>(provider =>
            {
                Assert.Same(services, provider);
                return new CallbackCommand(provider, () => invocations++);
            });

            implementation.Execute("callback", services);

            Assert.Equal(1, invocations);
        }

        [Fact]
        public void AttributeSelectedInvocationReceivesConstructorDependencies()
        {
            ServiceContainer services = CreateServices();
            ActivationState state = new();
            services.AddService(state);
            CommandService commands = new();
            commands.AddCommands<ProviderCommand>();

            commands.Execute("provider", services);

            Assert.Same(services, state.Provider);
        }

        [Fact]
        public void MissingInvocationAttributeIsRejectedAtRegistration()
        {
            CommandService commands = new();

            Assert.Contains("No command invoke method", Assert.Throws<ArgumentException>(() => commands.AddCommands<MissingInvocationCommand>()).Message);
        }

        [Fact]
        public void StaticInvocationReceivesProviderWithoutConstructingCommand()
        {
            ServiceContainer services = CreateServices();
            ActivationState state = new();
            services.AddService(state);
            CommandService commands = new();
            commands.AddCommands<StaticProviderCommand>();

            commands.Execute("static-provider", services);

            Assert.Same(services, state.Provider);
        }

        [Fact]
        public void MultipleInvocationAttributesAreRejectedAtRegistration()
        {
            CommandService commands = new();

            Assert.Contains("Multiple CommandInvokeAttribute", Assert.Throws<ArgumentException>(() => commands.AddCommands<MultipleInvocationCommand>()).Message);
        }

        [Fact]
        public void NullRegistrationFactoryIsRejected()
        {
            Assert.Equal("factory", Assert.Throws<ArgumentNullException>(() => new CommandService().AddCommands<CallbackCommand>(null)).ParamName);
        }

        [Fact]
        public void ConstructorRegistrationRejectsNullService()
        {
            ICommandService commands = null;

            Assert.Equal("commandService", Assert.Throws<ArgumentNullException>(() => commands.AddCommands<CallbackCommand>()).ParamName);
        }

        [Fact]
        public void UnsupportedCallbackSignaturesAreRejectedAtRegistration()
        {
            CommandService commands = new();

            Assert.Contains("must be non-generic and cannot take by-reference or pointer parameters",
                Assert.Throws<ArgumentException>(() => commands.AddCommands<InvalidInvocationCommand>()).Message);
            Assert.Contains("must be non-generic and cannot take by-reference or pointer parameters",
                Assert.Throws<ArgumentException>(() => commands.AddCommands<InvalidFilterCommand>()).Message);
            Assert.Contains("must be non-generic and cannot take by-reference or pointer parameters",
                Assert.Throws<ArgumentException>(() => commands.AddCommands<InvalidHelpCommand>()).Message);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StaticCallbacksInjectServicesAndHonorParameterDefaults(bool provideOptionalServices)
        {
            ServiceContainer services = CreateServices();
            IServiceProvider provider = new ProviderWithoutSelfService(services);
            OptionalDependency optional = provideOptionalServices ? new() : null;
            MethodInjectionState state = new()
            {
                Provider = provider,
                Optional = optional,
                Count = provideOptionalServices ? 42 : 7,
                Mode = provideOptionalServices ? CallbackMode.Alternate : CallbackMode.Default
            };
            services.AddService(state);
            if (provideOptionalServices)
            {
                services.AddService(optional);
                services.AddService(state.Count);
                services.AddService(state.Mode);
            }
            CommandService commands = new();
            commands.AddCommands<InjectedCallbacksCommand>();

            commands.Execute("injected", provider);
            Assert.Contains("Injected help > ", commands.GetDetailedHelp("injected", provider, int.MaxValue));

            Assert.Equal(new[] { "filter", "invoke", "filter", "help" }, state.Calls);
        }

        [Fact]
        public void CallbackServicesAreResolvedFromEachInvocationContext()
        {
            ServiceContainer parent = CreateServices();
            ActivationState parentState = new();
            parent.AddService(parentState);
            ServiceContainer child = new(parent);
            ActivationState childState = new();
            child.AddService(childState);
            CommandService commands = new();
            commands.AddCommands<ServiceInvocationCommand>();

            commands.Execute("service-invoke", parent);
            commands.Execute("service-invoke", child);

            Assert.Same(parent, parentState.Provider);
            Assert.Same(child, childState.Provider);
        }

        [Theory]
        [InlineData("service-invoke", false)]
        [InlineData("service-filter", false)]
        [InlineData("service-help", true)]
        public void MissingRequiredCallbackServicesFailClearly(string command, bool help)
        {
            ServiceContainer services = CreateServices();
            CommandService commands = new();
            commands.AddCommands<ServiceInvocationCommand>();
            commands.AddCommands<ServiceFilterCommand>();
            commands.AddCommands<ServiceHelpCommand>();

            DiagnosticsException exception = Assert.Throws<DiagnosticsException>(() =>
            {
                if (help)
                {
                    commands.GetDetailedHelp(command, services, int.MaxValue);
                }
                else
                {
                    commands.Execute(command, services);
                }
            });

            Assert.Contains(nameof(ActivationState), exception.Message);
            Assert.Contains("state parameter", exception.Message);
        }

        [Fact]
        public void CallbackServiceResolutionFailuresPropagate()
        {
            ServiceContainer services = new(CreateServices(), new Dictionary<Type, ServiceFactory>
            {
                [typeof(ActivationState)] = _ => throw new InvalidOperationException("Service creation failed")
            });
            CommandService commands = new();
            commands.AddCommands<ServiceInvocationCommand>();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => commands.Execute("service-invoke", services));

            Assert.Equal("Service creation failed", exception.Message);
        }

        [Fact]
        public void NullFactoryResultsAreRejected()
        {
            CommandService commands = new();
            commands.AddCommands<CallbackCommand>(_ => null);

            Assert.Contains("callback", Assert.Throws<DiagnosticsException>(() => commands.Execute("callback", CreateServices())).Message);
        }

        [Fact]
        public void InvocationExceptionsRetainOriginalTypeAndStack()
        {
            ServiceContainer services = CreateServices();
            CommandService commands = new();
            commands.AddCommands<ThrowingCommand>();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => commands.Execute("throw", services));

            Assert.Equal("Command failure", exception.Message);
            Assert.Contains(nameof(ThrowingCommand.ThrowFromCommand), exception.StackTrace);
        }

        [Fact]
        public void InheritedRuntimeFiltersDoNotActivateUnsupportedCommands()
        {
            ServiceContainer services = CreateServices();
            CommandService commands = new();
            commands.AddCommands<DumpAsyncCommand>();
            commands.AddCommands<TimersCommand>();

            Assert.Empty(commands.GetAllCommandHelp(services));
            Assert.Throws<CommandNotFoundException>(() => commands.Execute("dumpasync", services));
            Assert.Throws<CommandNotFoundException>(() => commands.Execute("timerinfo", services));
        }

        private static ServiceContainer CreateServices()
        {
            ServiceContainer services = new(null);
            services.AddService<IConsoleService>(new CaptureConsoleService());
            services.AddService<IHost>(new Host(HostType.DotnetDump));
            return services;
        }

        public sealed class ActivationState
        {
            public List<ActivatedCommand> Invocations { get; } = new();
            public IServiceProvider Provider { get; set; }
        }

        public sealed class OptionalDependency
        {
        }

        [Command(Name = "activate", Help = "Constructor command", DefaultOptions = "--count 3")]
        public sealed class ActivatedCommand : CommandBase
        {
            public ActivatedCommand(IServiceProvider services, ActivationState state, OptionalDependency optional = null)
                : base(services)
            {
                State = state;
                Optional = optional;
            }

            public ActivationState State { get; }
            public OptionalDependency Optional { get; }

            [Argument]
            public string Value { get; set; }

            [Option(Name = "--count")]
            public int Count { get; set; } = 1;

            public override void Invoke() => State.Invocations.Add(this);

            [HelpInvoke]
            public string GetHelp(ActivationState state)
            {
                Assert.Same(State, state);
                return $"Count: {Count}";
            }
        }

        [Command(Name = "activate", Help = "Unavailable command")]
        public sealed class UnavailableCommand : CommandBase
        {
            public UnavailableCommand(IServiceProvider services, OptionalDependency required)
                : base(services)
            {
                throw new InvalidOperationException("Unsupported command must not be constructed.");
            }

            public override void Invoke() => throw new InvalidOperationException();

            [FilterInvoke]
            public static bool Filter(IServiceProvider services) => services.GetService<OptionalDependency>() != null;
        }

        [Command(Name = "static-help")]
        public sealed class StaticHelpCommand : CommandBase
        {
            public StaticHelpCommand(IServiceProvider services, OptionalDependency required)
                : base(services)
            {
                throw new InvalidOperationException("Static help must not construct the command.");
            }

            public override void Invoke() => throw new InvalidOperationException();

            [HelpInvoke]
            public static string GetHelp(IServiceProvider services)
            {
                Assert.NotNull(services.GetRequiredService<IConsoleService>());
                return "Static help {prompt}";
            }
        }

        [Command(Name = "callback")]
        public sealed class CallbackCommand : CommandBase
        {
            private readonly Action _callback;

            public CallbackCommand(IServiceProvider services, Action callback)
                : base(services)
            {
                _callback = callback;
            }

            public override void Invoke() => _callback();
        }

        [Command(Name = "provider")]
        public sealed class ProviderCommand
        {
            private readonly IServiceProvider _services;

            public ProviderCommand(IServiceProvider services) => _services = services;

            [CommandInvoke]
            public void Run(ActivationState state, IServiceProvider services)
            {
                Assert.Same(_services, services);
                state.Provider = services;
            }
        }

        [Command(Name = "static-provider")]
        public sealed class StaticProviderCommand
        {
            public StaticProviderCommand() => throw new InvalidOperationException("Static invocation must not construct the command.");

            [CommandInvoke]
            public static void Invoke(IServiceProvider services) => services.GetRequiredService<ActivationState>().Provider = services;
        }

        [Command(Name = "missing-invocation")]
        public sealed class MissingInvocationCommand
        {
            public void Invoke() { }
        }

        [Command(Name = "multiple-invocations")]
        public sealed class MultipleInvocationCommand
        {
            [CommandInvoke]
            public void Invoke() { }

            [CommandInvoke]
            public void OtherInvoke() { }
        }

        [Command(Name = "invalid-invoke")]
        public sealed class InvalidInvocationCommand
        {
            [CommandInvoke]
            public void Invoke(ref ActivationState state) { }
        }

        [Command(Name = "invalid-filter")]
        public sealed class InvalidFilterCommand
        {
            [CommandInvoke]
            public void Invoke() { }

            [FilterInvoke]
            public static bool Filter(out ActivationState state)
            {
                state = null;
                return true;
            }
        }

        [Command(Name = "invalid-help")]
        public sealed class InvalidHelpCommand
        {
            [CommandInvoke]
            public void Invoke() { }

            [HelpInvoke]
            public static string GetHelp<T>(ActivationState state) => "";
        }

        public enum CallbackMode
        {
            Default,
            Alternate
        }

        public sealed class MethodInjectionState
        {
            public IServiceProvider Provider { get; set; }
            public OptionalDependency Optional { get; set; }
            public int Count { get; set; }
            public CallbackMode Mode { get; set; }
            public List<string> Calls { get; } = new();
        }

        [Command(Name = "injected")]
        public sealed class InjectedCallbacksCommand
        {
            public InjectedCallbacksCommand() => throw new InvalidOperationException("Static callbacks must not construct the command.");

            [CommandInvoke]
            public static void Invoke(MethodInjectionState state, IServiceProvider services, IConsoleService console,
                OptionalDependency optional = null, int count = 7, CallbackMode mode = CallbackMode.Default)
            {
                Assert.Same(services.GetRequiredService<IConsoleService>(), console);
                VerifyArguments("invoke", state, services, optional, count, mode);
            }

            [FilterInvoke]
            public static bool Filter(MethodInjectionState state, IServiceProvider services,
                OptionalDependency optional = null, int count = 7, CallbackMode mode = CallbackMode.Default)
            {
                VerifyArguments("filter", state, services, optional, count, mode);
                return true;
            }

            [HelpInvoke]
            public static string GetHelp(MethodInjectionState state, IServiceProvider services,
                OptionalDependency optional = null, int count = 7, CallbackMode mode = CallbackMode.Default)
            {
                VerifyArguments("help", state, services, optional, count, mode);
                return "Injected help {prompt}";
            }

            private static void VerifyArguments(string callback, MethodInjectionState state, IServiceProvider services,
                OptionalDependency optional, int count, CallbackMode mode)
            {
                Assert.Same(state.Provider, services);
                Assert.Same(state.Optional, optional);
                Assert.Equal(state.Count, count);
                Assert.Equal(state.Mode, mode);
                state.Calls.Add(callback);
            }
        }

        [Command(Name = "service-invoke")]
        public sealed class ServiceInvocationCommand
        {
            [CommandInvoke]
            public static void Invoke(ActivationState state, IServiceProvider services) => state.Provider = services;
        }

        [Command(Name = "service-filter")]
        public sealed class ServiceFilterCommand
        {
            [CommandInvoke]
            public void Invoke() { }

            [FilterInvoke]
            public static bool Filter(ActivationState state) => true;
        }

        [Command(Name = "service-help")]
        public sealed class ServiceHelpCommand
        {
            [CommandInvoke]
            public void Invoke() { }

            [HelpInvoke]
            public static string GetHelp(ActivationState state) => "Service help";
        }

        private sealed class ProviderWithoutSelfService : IServiceProvider
        {
            private readonly IServiceProvider _services;

            public ProviderWithoutSelfService(IServiceProvider services) => _services = services;

            public object GetService(Type serviceType) => serviceType == typeof(IServiceProvider) ? null : _services.GetService(serviceType);
        }

        [Command(Name = "throw")]
        public sealed class ThrowingCommand : CommandBase
        {
            public ThrowingCommand(IServiceProvider services)
                : base(services)
            {
            }

            public override void Invoke() => ThrowFromCommand();

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void ThrowFromCommand() => throw new InvalidOperationException("Command failure");
        }
    }
}
