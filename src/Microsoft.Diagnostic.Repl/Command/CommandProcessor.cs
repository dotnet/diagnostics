// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Diagnostic.Repl
{
    public class CommandProcessor
    {
        private readonly Parser _parser;
        private readonly Command _rootCommand;
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        /// <summary>
        /// Create an instance of the command processor;
        /// </summary>
        /// <param name="assemblies">The list of assemblies to look for commands</param>
        public CommandProcessor(IEnumerable<Assembly> assemblies)
        {
            _services.Add(typeof(CommandProcessor), this);
            var rootBuilder = new CommandLineBuilder();
            rootBuilder.UseHelp()
                       .UseParseDirective()
                       .UseSuggestDirective()
                       .UseParseErrorReporting()
                       .UseExceptionHandler();
            BuildCommands(rootBuilder, assemblies);
            _rootCommand = rootBuilder.Command;
            _parser = rootBuilder.Build();
        }

        /// <summary>
        /// Adds a service or context to inject into an command.
        /// </summary>
        /// <typeparam name="T">type of service</typeparam>
        /// <param name="instance">service instance</param>
        public void AddService<T>(T instance)
        {
            AddService(typeof(T), instance);
        }

        /// <summary>
        /// Adds a service or context to inject into an command.
        /// </summary>
        /// <param name="type">service type</param>
        /// <param name="instance">service instance</param>
        public void AddService(Type type, object instance)
        {
            _services.Add(type, instance);
        }

        /// <summary>
        /// Parse the command line.
        /// </summary>
        /// <param name="commandLine">command line txt</param>
        /// <param name="console">option console</param>
        /// <returns>exit code</returns>
        public Task<int> Parse(string commandLine, IConsole console = null)
        {
            ParseResult result = _parser.Parse(commandLine);
            return _parser.InvokeAsync(result, console);
        }

        /// <summary>
        /// Display all the help or a specific command's help.
        /// </summary>
        /// <param name="name">command name or null</param>
        /// <returns>command instance or null if not found</returns>
        public Command GetCommand(string name)
        {
            if (string.IsNullOrEmpty(name)) {
                return _rootCommand;
            }
            else {
                return _rootCommand.Children.OfType<Command>().FirstOrDefault((cmd) => name == cmd.Name || cmd.Aliases.Any((alias) => name == alias));
            }
        }

        private void BuildCommands(CommandLineBuilder rootBuilder, IEnumerable<Assembly> assemblies)
        {
            foreach (Type type in assemblies.SelectMany((assembly) => assembly.GetExportedTypes()))
            {
                Command command = null;

                var baseAttributes = (BaseAttribute[])type.GetCustomAttributes(typeof(BaseAttribute), inherit: false);
                foreach (BaseAttribute baseAttribute in baseAttributes)
                {
                    if (baseAttribute is CommandAttribute commandAttribute)
                    {
                        command = new Command(commandAttribute.Name, commandAttribute.Help);
                        var builder = new CommandLineBuilder(command);
                        builder.UseHelp();

                        var properties = new List<(PropertyInfo, Option)>();
                        PropertyInfo argument = null;

                        foreach (PropertyInfo property in type.GetProperties().Where(p => p.CanWrite))
                        {
                            var argumentAttribute = (ArgumentAttribute)property.GetCustomAttributes(typeof(ArgumentAttribute), inherit: false).SingleOrDefault();
                            if (argumentAttribute != null)
                            {
                                if (argument != null) {
                                    throw new ArgumentException($"More than one ArgumentAttribute in command class: {type.Name}");
                                }
                                command.Argument = new Argument {
                                    Name = argumentAttribute.Name ?? property.Name.ToLowerInvariant(),
                                    Description = argumentAttribute.Help,
                                    ArgumentType = property.PropertyType,
                                    Arity = new ArgumentArity(0, int.MaxValue)
                                };
                                argument = property;
                            }
                            else
                            {
                                var optionAttribute = (OptionAttribute)property.GetCustomAttributes(typeof(OptionAttribute), inherit: false).SingleOrDefault();
                                if (optionAttribute != null)
                                {
                                    var option = new Option(optionAttribute.Name ?? BuildAlias(property.Name), optionAttribute.Help, new Argument { ArgumentType = property.PropertyType });
                                    command.AddOption(option);
                                    properties.Add((property, option));

                                    foreach (var optionAliasAttribute in (OptionAliasAttribute[])property.GetCustomAttributes(typeof(OptionAliasAttribute), inherit: false))
                                    {
                                        option.AddAlias(optionAliasAttribute.Name);
                                    }
                                }
                                else
                                {
                                    // If not an option, add as just a settable properties
                                    properties.Add((property, null));
                                }
                            }
                        }

                        command.Handler = new Handler(this, commandAttribute.AliasExpansion, argument, properties, type);
                        rootBuilder.AddCommand(command);
                    }

                    if (baseAttribute is CommandAliasAttribute commandAliasAttribute)
                    {
                        if (command == null) {
                            throw new ArgumentException($"No previous CommandAttribute for this CommandAliasAttribute: {type.Name}");
                        }
                        command.AddAlias(commandAliasAttribute.Name);
                    }
                }
            }
        }

        private static string BuildAlias(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(parameterName));
            }
            return parameterName.Length > 1 ? $"--{parameterName.ToKebabCase()}" : $"-{parameterName.ToLowerInvariant()}";
        }

        class Handler : ICommandHandler
        {
            private readonly CommandProcessor _commandProcessor;
            private readonly string _aliasExpansion;
            private readonly PropertyInfo _argument;
            private readonly IEnumerable<(PropertyInfo Property, Option Option)> _properties;

            private readonly ConstructorInfo _constructor;
            private readonly MethodInfo _methodInfo;

            public Handler(CommandProcessor commandProcessor, string aliasExpansion, PropertyInfo argument, IEnumerable<(PropertyInfo, Option)> properties, Type type)
            {
                _commandProcessor = commandProcessor;
                _aliasExpansion = aliasExpansion;
                _argument = argument;
                _properties = properties;

                _constructor = type.GetConstructors().SingleOrDefault((info) => info.GetParameters().Length == 0) ?? 
                    throw new ArgumentException($"No eligible constructor found in {type}");

                _methodInfo = type.GetMethod(CommandBase.EntryPointName, new Type[] { typeof(IHelpBuilder) }) ?? type.GetMethod(CommandBase.EntryPointName) ?? 
                    throw new ArgumentException($"{CommandBase.EntryPointName} method not found in {type}");
            }

            public Task<int> InvokeAsync(InvocationContext context)
            {
                try
                {
                    // Assumes zero parameter constructor
                    object instance = _constructor.Invoke(new object[0]);
                    SetProperties(context, instance);

                    var methodBinder = new MethodBinder(_methodInfo, () => instance);
                    return methodBinder.InvokeAsync(context);
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException;
                }
            }

            private void SetProperties(InvocationContext context, object instance)
            {
                IEnumerable<OptionResult> optionResults = context.ParseResult.CommandResult.Children.OfType<OptionResult>();

                foreach ((PropertyInfo Property, Option Option) property in _properties)
                {
                    object value = property.Property.GetValue(instance);

                    if (property.Property.Name == nameof(CommandBase.AliasExpansion)) {
                        value = _aliasExpansion;
                    }
                    else 
                    {
                        Type propertyType = property.Property.PropertyType;
                        if (propertyType == typeof(InvocationContext)) {
                            value = context;
                        }
                        else if (propertyType == typeof(IConsole)) {
                            value = context.Console;
                        }
                        else if (_commandProcessor._services.TryGetValue(propertyType, out object service)) {
                            value = service;
                        }
                        else if (property.Option != null)
                        {
                            OptionResult optionResult = optionResults.Where((result) => result.Option == property.Option).SingleOrDefault();
                            if (optionResult != null) {
                                value = optionResult.GetValueOrDefault();
                            }
                        }
                    }

                    property.Property.SetValue(instance, value);
                }

                if (_argument != null)
                {
                    object value = context.ParseResult.CommandResult.GetValueOrDefault();
                    _argument.SetValue(instance, value);
                }
            }
        }
    }
}
