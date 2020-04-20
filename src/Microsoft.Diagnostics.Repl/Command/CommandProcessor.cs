// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Repl
{
    public class CommandProcessor
    {
        private readonly Parser _parser;
        private readonly Command _rootCommand;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConsole _console;
        private readonly Dictionary<string, Handler> _commandHandlers = new Dictionary<string, Handler>();

        /// <summary>
        /// Create an instance of the command processor;
        /// </summary>
        /// <param name="serviceProvider">service provider interface</param>
        /// <param name="console">console instance</param>
        /// <param name="assemblies">Optional list of assemblies to look for commands</param>
        /// <param name="types">Optional list of types to look for commands</param>
        public CommandProcessor(IServiceProvider serviceProvider, IConsole console, IEnumerable<Assembly> assemblies = null, IEnumerable<Type> types = null)
        {
            Debug.Assert(serviceProvider != null);
            Debug.Assert(assemblies != null);
            _serviceProvider = serviceProvider;
            _console = console;

            var rootBuilder = new CommandLineBuilder(new Command(">"));
            rootBuilder.UseHelp()
                       .UseHelpBuilder((bindingContext) => GetService<IHelpBuilder>())
                       .UseParseDirective()
                       .UseSuggestDirective()
                       .UseParseErrorReporting()
                       .UseExceptionHandler();

            if (assemblies != null) {
                BuildCommands(rootBuilder, assemblies);
            }
            if (types != null) {
                BuildCommands(rootBuilder, types);
            }
            _rootCommand = rootBuilder.Command;
            _parser = rootBuilder.Build();
        }

        /// <summary>
        /// Creates a new instance of the command help builder
        /// </summary>
        public IHelpBuilder CreateHelpBuilder()
        {
            return new LocalHelpBuilder(this);
        }

        /// <summary>
        /// Parse the command line.
        /// </summary>
        /// <param name="commandLine">command line txt</param>
        /// <returns>exit code</returns>
        public Task<int> Parse(string commandLine)
        {
            ParseResult result = _parser.Parse(commandLine);
            return _parser.InvokeAsync(result, _console);
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
            BuildCommands(rootBuilder, assemblies.SelectMany((assembly) => assembly.GetExportedTypes()));
        }

        private void BuildCommands(CommandLineBuilder rootBuilder, IEnumerable<Type> types)
        {
            foreach (Type type in types)
            {
                for (Type baseType = type; baseType != null; baseType = baseType.BaseType)
                {
                    if (baseType == typeof(CommandBase)) {
                        break;
                    }
                    BuildCommands(rootBuilder, baseType);
                }
            }
        }

        private void BuildCommands(CommandLineBuilder rootBuilder, Type type)
        {
            Command command = null;

            var baseAttributes = (BaseAttribute[])type.GetCustomAttributes(typeof(BaseAttribute), inherit: false);
            foreach (BaseAttribute baseAttribute in baseAttributes)
            {
                if (baseAttribute is CommandAttribute commandAttribute && IsValidPlatform(commandAttribute))
                {
                    command = new Command(commandAttribute.Name, commandAttribute.Help);
                    var properties = new List<(PropertyInfo, Option)>();
                    var arguments = new List<(PropertyInfo, Argument)>();

                    foreach (PropertyInfo property in type.GetProperties().Where(p => p.CanWrite))
                    {
                        var argumentAttribute = (ArgumentAttribute)property.GetCustomAttributes(typeof(ArgumentAttribute), inherit: false).SingleOrDefault();
                        if (argumentAttribute != null)
                        {
                            IArgumentArity arity = property.PropertyType.IsArray ? ArgumentArity.ZeroOrMore : ArgumentArity.ZeroOrOne;

                            var argument = new Argument {
                                Name = argumentAttribute.Name ?? property.Name.ToLowerInvariant(),
                                Description = argumentAttribute.Help,
                                ArgumentType = property.PropertyType,
                                Arity = arity
                            };
                            command.AddArgument(argument);
                            arguments.Add((property, argument));
                        }
                        else
                        {
                            var optionAttribute = (OptionAttribute)property.GetCustomAttributes(typeof(OptionAttribute), inherit: false).SingleOrDefault();
                            if (optionAttribute != null)
                            {
                                var option = new Option(optionAttribute.Name ?? BuildAlias(property.Name), optionAttribute.Help) {
                                    Argument = new Argument { ArgumentType = property.PropertyType }
                                };
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

                    var handler = new Handler(this, commandAttribute.AliasExpansion, arguments, properties, type);
                    _commandHandlers.Add(command.Name, handler);
                    command.Handler = handler;

                    rootBuilder.AddCommand(command);
                }

                if (baseAttribute is CommandAliasAttribute commandAliasAttribute && IsValidPlatform(commandAliasAttribute))
                {
                    if (command == null)
                    {
                        throw new ArgumentException($"No previous CommandAttribute for this CommandAliasAttribute: {type.Name}");
                    }
                    command.AddAlias(commandAliasAttribute.Name);
                }
            }
        }

        private object GetService(Type serviceType)
        {
            return _serviceProvider.GetService(serviceType);
        }

        private T GetService<T>()
        {
            T service = (T)_serviceProvider.GetService(typeof(T));
            Debug.Assert(service != null);
            return service;
        }

        /// <summary>
        /// Returns true if the command should be added.
        /// </summary>
        private static bool IsValidPlatform(CommandBaseAttribute attribute)
        {
            if (attribute.Platform != CommandPlatform.All)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return (attribute.Platform & CommandPlatform.Windows) != 0;
                }
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return (attribute.Platform & CommandPlatform.Linux) != 0;
                }
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return (attribute.Platform & CommandPlatform.OSX) != 0;
                }
            }
            return true;
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
            private readonly IEnumerable<(PropertyInfo Property, Argument Argument)> _arguments;
            private readonly IEnumerable<(PropertyInfo Property, Option Option)> _properties;

            private readonly ConstructorInfo _constructor;
            private readonly MethodInfo _methodInfo;
            private readonly MethodInfo _methodInfoHelp;

            public Handler(CommandProcessor commandProcessor, string aliasExpansion, IEnumerable<(PropertyInfo, Argument)> arguments, IEnumerable<(PropertyInfo, Option)> properties, Type type)
            {
                _commandProcessor = commandProcessor;
                _aliasExpansion = aliasExpansion;
                _arguments = arguments;
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
                    object instance = _constructor.Invoke(Array.Empty<object>());
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
                        object service = GetService(propertyType, context);
                        if (service != null) {
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

                if (context != null)
                {
                    IEnumerable<ArgumentResult> argumentResults = context.ParseResult.CommandResult.Children.OfType<ArgumentResult>();

                    foreach ((PropertyInfo Property, Argument Argument) argument in _arguments)
                    {
                        ArgumentResult argumentResult = argumentResults.Where((result) => result.Argument == argument.Argument).SingleOrDefault();
                        if (argumentResult != null)
                        {
                            object value = argumentResult.GetValueOrDefault();
                            argument.Property.SetValue(instance, value);
                        }
                    }
                }
            }

            private object[] BuildArguments(MethodBase methodBase, InvocationContext context)
            {
                ParameterInfo[] parameters = methodBase.GetParameters();
                object[] arguments = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++) {
                    Type parameterType = parameters[i].ParameterType;
                    // The parameter will passed as null to allow for "optional" services. The invoked 
                    // method needs to check for possible null parameters.
                    arguments[i] = GetService(parameterType, context);
                }
                return arguments;
            }

            private object GetService(Type type, InvocationContext context)
            {
                object service;
                if (type == typeof(InvocationContext)) {
                    service = context;
                }
                else {
                    service = _commandProcessor.GetService(type);
                }
                return service;
            }
        }

        class LocalHelpBuilder : IHelpBuilder
        {
            private readonly CommandProcessor _commandProcessor;

            public LocalHelpBuilder(CommandProcessor commandProcessor)
            {
                _commandProcessor = commandProcessor;
            }

            void IHelpBuilder.Write(ICommand command)
            {
                if (_commandProcessor._commandHandlers.TryGetValue(command.Name, out Handler handler))
                {
                    if (handler.InvokeHelp()) {
                        return;
                    }
                }
                var helpBuilder = new HelpBuilder(_commandProcessor._console, maxWidth: Console.WindowWidth);
                helpBuilder.Write(command);
            }
        }
    }
}
