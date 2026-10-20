// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.ExtensionCommands;
using SOS.Hosting;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class CommandRegistryTests
    {
        [Fact]
        public void AnalysisCommandsRegisterEveryCommandType()
        {
            AssertRegistration(typeof(ClrMDHelper).Assembly, new AnalysisModule().RegisterCommands);
        }

        [Fact]
        public void HostingCommandsRegisterEveryCommandType()
        {
            AssertRegistration(typeof(SOSHost).Assembly, new SOSHostingModule().RegisterCommands);
        }

        [Fact]
        public void CommandRegistrarsRejectNullService()
        {
            Assert.Equal("commandService", Assert.Throws<ArgumentNullException>(() => new AnalysisModule().RegisterCommands(null)).ParamName);
            Assert.Equal("commandService", Assert.Throws<ArgumentNullException>(() => new SOSHostingModule().RegisterCommands(null)).ParamName);
            Assert.Equal("commandService", Assert.Throws<ArgumentNullException>(() => new CoreModule().RegisterCommands(null)).ParamName);
        }

        [Fact]
        public void BuiltInModulesRegisterBeforeInitializerModules()
        {
            RecordingCommandService expected = new();
            new SOSHostingModule().RegisterCommands(expected);
            new AnalysisModule().RegisterCommands(expected);
            new DiagnosticsModuleTests.TestModule().RegisterCommands(expected);
            RecordingCommandService actual = new();
            ServiceManager manager = new();
            manager.RegisterModule(new SOSHostingModule());
            manager.RegisterModule(new AnalysisModule());
            manager.RegisterModule(new SOSHostingModule());
            manager.RegisterModule(new AnalysisModule());
            manager.RegisterModules(DiagnosticsModuleCatalog.GetFactories());
            manager.RegisterModules(DiagnosticsModuleCatalog.GetFactories());
            manager.FinalizeServices();
            manager.RegisterCommands(actual);
            manager.RegisterCommands(actual);
            Assert.Equal(expected.Types, actual.Types);

            CommandService baseline = new();
            new SOSHostingModule().RegisterCommands(baseline);
            new AnalysisModule().RegisterCommands(baseline);
            new DiagnosticsModuleTests.TestModule().RegisterCommands(baseline);
            CommandService combined = new();
            manager.RegisterCommands(combined);
            Assert.Equal(GetInventory(baseline), GetInventory(combined));
            Assert.Equal(GetFallbackInventory(baseline), GetFallbackInventory(combined));
        }

        private static void AssertRegistration(Assembly assembly, Action<ICommandService> register)
        {
            // Discover the expected inventory only in this test so an omitted registration fails.
            Type[] expected = assembly.GetExportedTypes()
                .Where(type => type.IsDefined(typeof(CommandAttribute), inherit: false))
                .ToArray();
            RecordingCommandService recording = new();
            register(recording);
            Assert.Equal(expected.OrderBy(type => type.FullName), recording.Types.OrderBy(type => type.FullName));

            CommandService baseline = new();
            MethodInfo addCommands = typeof(CommandServiceExtensions).GetMethod(nameof(CommandServiceExtensions.AddCommands));
            foreach (Type type in expected)
            {
                addCommands.MakeGenericMethod(type).Invoke(null, new object[] { baseline });
            }
            CommandService actual = new();
            register(actual);
            Assert.Equal(GetInventory(baseline), GetInventory(actual));
            Assert.Equal(GetFallbackInventory(baseline), GetFallbackInventory(actual));
        }

        private static string[] GetInventory(ICommandService commands) =>
            commands.Commands
                .Select(command => $"{command.name}\n{command.help}\n{string.Join(";", command.aliases ?? Array.Empty<string>())}")
                .OrderBy(command => command, StringComparer.Ordinal)
                .ToArray();

        private static string[] GetFallbackInventory(ICommandService commands) =>
            commands.Commands
                .GroupBy(command => command.name)
                .Where(group => group.Count() > 1)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .SelectMany(group => group.Select(command => $"{command.name}\n{command.help}"))
                .ToArray();

        private sealed class RecordingCommandService : ICommandService
        {
            public List<Type> Types { get; } = new();
            public IEnumerable<(string name, string help, IEnumerable<string> aliases)> Commands => throw new NotSupportedException();
            public void AddCommands<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods |
                DynamicallyAccessedMemberTypes.PublicProperties)] TCommand>(
                Func<IServiceProvider, TCommand> factory) where TCommand : class => Types.Add(typeof(TCommand));
            public IEnumerable<(string Invocation, string Help)> GetAllCommandHelp(IServiceProvider services) => throw new NotSupportedException();
            public string GetDetailedHelp(string commandName, IServiceProvider services, int consoleWidth) => throw new NotSupportedException();
        }
    }
}
