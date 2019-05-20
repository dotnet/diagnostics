// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Diagnostics;
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
        private readonly Dictionary<string, Handler> _commandHandlers = new Dictionary<string, Handler>();

        /// <summary>
        /// Create an instance of the command processor;
        /// </summary>
        /// <param name="console">console instance to use for commands</param>
        /// <param name="assemblies">The list of assemblies to look for commands</param>
        public CommandProcessor(IConsole console, IEnumerable<Assembly> assemblies)
        {
            Debug.Assert(console != null);
            Debug.Assert(assemblies != null);
            _services.Add(typeof(CommandProcessor), this);
            _services.Add(typeof(IConsole), console);
            _services.Add(typeof(IHelpBuilder), new LocalHelpBuilder(this));
            var rootBuilder = new CommandLineBuilder(new Command(">"));
            rootBuilder.UseHelp()
                       .UseHelpBuilder((bindingContext) => GetService<IHelpBuilder>())
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
        /// <returns>exit code</returns>
        public Task<int> Parse(string commandLine)
        {
            ParseResult result = _parser.Parse(commandLine);
            return _parser.InvokeAsync(result, GetService<IConsole>());
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
                                IArgumentArity arity = property.PropertyType.IsArray ? ArgumentArity.ZeroOrMore : ArgumentArity.ZeroOrOne;

                                command.Argument = new Argument {
                                    Name = argumentAttribute.Name ?? property.Name.ToLowerInvariant(),
                                    Description = argumentAttribute.Help,
                                    ArgumentType = property.PropertyType,
                                    Arity = arity
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

                        var handler = new Handler(this, commandAttribute.AliasExpansion, argument, properties, type);
                        _commandHandlers.Add(command.Name, handler);
                        command.Handler = handler;

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

        private T GetService<T>()
        {
            _services.TryGetValue(typeof(T), out object service);
            Debug.Assert(service != null);
            return (T)service;
        }

        private static string BuildAlias(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) {
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
            private readonly MethodInfo _methodInfoHelp;

            public Handler(CommandProcessor commandProcessor, string aliasExpansion, PropertyInfo argument, IEnumerable<(PropertyInfo, Option)> properties, Type type)
            {
                _commandProcessor = commandProcessor;
                _aliasExpansion = aliasExpansion;
                _argument = argument;
                _properties = properties;

                _constructor = type.GetConstructors().SingleOrDefault((info) => info.GetParameters().Length == 0) ?? 
                    throw new ArgumentException($"No eligible constructor found in {type}");

                _methodInfo = type.GetMethods().Where((methodInfo) => methodInfo.GetCustomAttribute<CommandInvokeAttribute>() != null).SingleOrDefault() ??
                    throw new ArgumentException($"No command invoke method found in {type}");

                _methodInfoHelp = type.GetMethods().Where((methodInfo) => methodInfo.GetCustomAttribute<HelpInvokeAttribute>() != null).SingleOrDefault();
            }

            Task<int> ICommandHandler.InvokeAsync(InvocationContext context)
            {
                try
                {
                    Invoke(_methodInfo, context);
                }
                catch (Exception ex)
                {
                    return Task.FromException<int>(ex);
                }
                return Task.FromResult(context.ResultCode);
            }

            /// <summary>
            /// Executes the command's help invoke function if exists
            /// </summary>
            /// <returns>true help called, false no help function</returns>
            internal bool InvokeHelp()
            {
                if (_methodInfoHelp == null)
                {
                    return false;
                }
                // The InvocationContext is null so the options and arguments in the 
                // command instance created don't get set. The context for the command
                // requesting help (either the help command or some other command using
                // --help) won't work for the command instance that implements it's own
                // help (SOS command).
                Invoke(_methodInfoHelp, context: null);
                return true;
            }

            private void Invoke(MethodInfo methodInfo, InvocationContext context)
            {
                try
                {
                    // Assumes zero parameter constructor
                    object instance = _constructor.Invoke(new object[0]);
                    SetProperties(context, instance);

                    object[] arguments = BuildArguments(methodInfo, context);
                    methodInfo.Invoke(instance, arguments);
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException;
                }
            }

            private void SetProperties(InvocationContext context, object instance)
            {
                IEnumerable<OptionResult> optionResults = context?.ParseResult.CommandResult.Children.OfType<OptionResult>();

                foreach ((PropertyInfo Property, Option Option) property in _properties)
                {
                    object value = property.Property.GetValue(instance);

                    if (property.Property.Name == nameof(CommandBase.AliasExpansion)) {
                        value = _aliasExpansion;
                    }
                    else 
                    {
                        Type propertyType = property.Property.PropertyType;
                        if (TryGetService(propertyType, context, out object service)) {
                            value = service;
                        }
                        else if (context != null && property.Option != null)
                        {
                            OptionResult optionResult = optionResults.Where((result) => result.Option == property.Option).SingleOrDefault();
                            if (optionResult != null) {
                                value = optionResult.GetValueOrDefault();
                            }
                        }
                    }

                    property.Property.SetValue(instance, value);
                }

                if (context != null && _argument != null)
                {
                    object value = null;
                    ArgumentResult result = context.ParseResult.CommandResult.ArgumentResult;
                    switch (result)
                    {
                        case SuccessfulArgumentResult successful:
                            value = successful.Value;
                            break;
                        case FailedArgumentResult failed:
                            throw new InvalidOperationException(failed.ErrorMessage);
                    }
                    _argument.SetValue(instance, value);
                }
            }

            private object[] BuildArguments(MethodBase methodBase, InvocationContext context)
            {
                ParameterInfo[] parameters = methodBase.GetParameters();
                object[] arguments = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++) {
                    Type parameterType = parameters[i].ParameterType;
                    // Ignoring false: the parameter will passed as null to allow for "optional"
                    // services. The invoked method needs to check for possible null parameters.
                    TryGetService(parameterType, context, out arguments[i]);
                }
                return arguments;
            }

            private bool TryGetService(Type type, InvocationContext context, out object service)
            {
                if (type == typeof(InvocationContext)) {
                    service = context;
                }
                else if (!_commandProcessor._services.TryGetValue(type, out service)) {
                    service = null;
                    return false;
                }
                return true;
            }
        }

        class LocalHelpBuilder : IHelpBuilder
        {
            private readonly CommandProcessor _commandProcessor;
            private readonly HelpBuilder _helpBuilder;

            public LocalHelpBuilder(CommandProcessor commandProcessor)
            {
                _commandProcessor = commandProcessor;
                _helpBuilder = new HelpBuilder(commandProcessor.GetService<IConsole>(), maxWidth: Console.WindowWidth);
            }

            void IHelpBuilder.Write(ICommand command)
            {
                if (_commandProcessor._commandHandlers.TryGetValue(command.Name, out Handler handler))
                {
                    if (handler.InvokeHelp()) {
                        return;
                    }
                }
                _helpBuilder.Write(command);
            }
        }
    }
}
