// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Implements the ICommandService interface using System.CommandLine.
    /// </summary>
    public class CommandService : ICommandService
    {
        private Parser _parser;
        private readonly CommandLineBuilder _rootBuilder;
        private readonly Dictionary<string, CommandHandler> _commandHandlers = new Dictionary<string, CommandHandler>();

        /// <summary>
        /// Create an instance of the command processor;
        /// </summary>
        /// <param name="commandPrompt">command prompted used in help message</param>
        public CommandService(string commandPrompt = null)
        {
            _rootBuilder = new CommandLineBuilder(new Command(commandPrompt ?? ">"));
            _rootBuilder.UseHelpBuilder((bindingContext) => new LocalHelpBuilder(this, bindingContext.Console, useHelpBuilder: false));
        }

        /// <summary>
        /// Parse and execute the command line.
        /// </summary>
        /// <param name="commandLine">command line text</param>
        /// <param name="services">services for the command</param>
        /// <returns>exit code</returns>
        public int Execute(string commandLine, IServiceProvider services)
        {
            // Parse the command line and invoke the command
            ParseResult parseResult = Parser.Parse(commandLine);

            var context = new InvocationContext(parseResult, new LocalConsole(services));
            if (parseResult.Errors.Count > 0)
            {
                context.InvocationResult = new ParseErrorResult();
            }
            else
            {
                if (parseResult.CommandResult.Command is Command command)
                {
                    if (command.Handler is CommandHandler handler)
                    {
                        ITarget target = services.GetService<ITarget>();
                        if (!handler.IsValidPlatform(target))
                        { 
                            if (target != null)
                            {
                                context.Console.Error.WriteLine($"Command '{command.Name}' not supported on this target");
                            }
                            else
                            {
                                context.Console.Error.WriteLine($"Command '{command.Name}' needs a target");
                            }
                            return 1;
                        }
                        try
                        {
                            handler.Invoke(context, services);
                        }
                        catch (Exception ex)
                        {
                            OnException(ex, context);
                        }
                    }
                }
            }

            context.InvocationResult?.Apply(context);
            return context.ResultCode;
        }

        /// <summary>
        /// Displays the help for a command
        /// </summary>
        /// <param name="commandName">name of the command or alias</param>
        /// <param name="services">service provider</param>
        /// <returns>true if success, false if command not found</returns>
        public bool DisplayHelp(string commandName, IServiceProvider services)
        {
            Command command = null;
            if (!string.IsNullOrEmpty(commandName)) 
            {
                command = _rootBuilder.Command.Children.OfType<Command>().FirstOrDefault((cmd) => commandName == cmd.Name || cmd.Aliases.Any((alias) => commandName == alias));
                if (command == null)
                {
                    return false;
                }
                if (command.Handler is CommandHandler handler)
                {
                    ITarget target = services.GetService<ITarget>();
                    if (!handler.IsValidPlatform(target))
                    {
                        return false;
                    }
                }
            }
            else 
            {
                ITarget target = services.GetService<ITarget>();

                // Create temporary builder adding only the commands that are valid for the target
                var builder = new CommandLineBuilder(new Command(_rootBuilder.Command.Name));
                foreach (Command cmd in _rootBuilder.Command.Children.OfType<Command>())
                {
                    if (cmd.Handler is CommandHandler handler)
                    {
                        if (handler.IsValidPlatform(target))
                        {
                            builder.AddCommand(cmd);
                        }
                    }
                }
                command = builder.Command;
            }
            Debug.Assert(command != null);
            IHelpBuilder helpBuilder = new LocalHelpBuilder(this, new LocalConsole(services), useHelpBuilder: true);
            helpBuilder.Write(command);
            return true;
        }

        /// <summary>
        /// Enumerates all the command's name and help
        /// </summary>
        public IEnumerable<(string name, string help, IEnumerable<string> aliases)> Commands => _commandHandlers.Select((keypair) => (keypair.Value.Name, keypair.Value.Help, keypair.Value.Aliases));

        /// <summary>
        /// Add the commands and aliases attributes found in the type.
        /// </summary>
        /// <param name="type">Command type to search</param>
        /// <param name="factory">function to create command instance</param>
        public void AddCommands(Type type, Func<IServiceProvider, object> factory)
        {
            for (Type baseType = type; baseType != null; baseType = baseType.BaseType)
            {
                if (baseType == typeof(CommandBase)) {
                    break;
                }
                var commandAttributes = (CommandAttribute[])baseType.GetCustomAttributes(typeof(CommandAttribute), inherit: false);
                foreach (CommandAttribute commandAttribute in commandAttributes)
                {
                    if (factory == null)
                    {
                        // Assumes zero parameter constructor
                        ConstructorInfo constructor = type.GetConstructors().SingleOrDefault((info) => info.GetParameters().Length == 0) ??
                            throw new ArgumentException($"No eligible constructor found in {type}");

                        factory = (services) => constructor.Invoke(Array.Empty<object>());
                    }
                    CreateCommand(baseType, commandAttribute, factory);
                }
            }

            // Build or re-build parser instance after all these commands and aliases are added
            _parser = null;
        }

        private void CreateCommand(Type type, CommandAttribute commandAttribute, Func<IServiceProvider, object> factory)
        {
            var command = new Command(commandAttribute.Name, commandAttribute.Help);
            var properties = new List<(PropertyInfo, Option)>();
            var arguments = new List<(PropertyInfo, Argument)>();

            foreach (string alias in commandAttribute.Aliases)
            {
                command.AddAlias(alias);
            }

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
                        var option = new Option(optionAttribute.Name ?? BuildOptionAlias(property.Name), optionAttribute.Help) {
                            Argument = new Argument { ArgumentType = property.PropertyType }
                        };
                        command.AddOption(option);
                        properties.Add((property, option));

                        foreach (string alias in optionAttribute.Aliases)
                        {
                            option.AddAlias(alias);
                        }
                    }
                    else
                    {
                        // If not an option, add as just a settable properties
                        properties.Add((property, null));
                    }
                }
            }

            var handler = new CommandHandler(commandAttribute, arguments, properties, type, factory);
            _commandHandlers.Add(command.Name, handler);
            command.Handler = handler;
            _rootBuilder.AddCommand(command);
        }

        private Parser Parser => _parser ??= _rootBuilder.Build();

        private void OnException(Exception ex, InvocationContext context)
        {
            if (ex is TargetInvocationException)
            {
                ex = ex.InnerException;
            }
            if (ex is NullReferenceException || 
                ex is ArgumentException || 
                ex is ArgumentNullException || 
                ex is ArgumentOutOfRangeException || 
                ex is NotImplementedException) 
            {
                context.Console.Error.WriteLine(ex.ToString());
            }
            else 
            {
                context.Console.Error.WriteLine(ex.Message);
            }
            Trace.TraceError(ex.ToString());
        }

        private static string BuildOptionAlias(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) {
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(parameterName));
            }
            return parameterName.Length > 1 ? $"--{parameterName.ToKebabCase()}" : $"-{parameterName.ToLowerInvariant()}";
        }

        /// <summary>
        /// The normal command handler.
        /// </summary>
        class CommandHandler : ICommandHandler
        {
            private readonly CommandAttribute _commandAttribute;
            private readonly IEnumerable<(PropertyInfo Property, Argument Argument)> _arguments;
            private readonly IEnumerable<(PropertyInfo Property, Option Option)> _properties;

            private readonly Func<IServiceProvider, object> _factory;
            private readonly MethodInfo _methodInfo;
            private readonly MethodInfo _methodInfoHelp;

            public CommandHandler(
                CommandAttribute commandAttribute,
                IEnumerable<(PropertyInfo, Argument)> arguments, 
                IEnumerable<(PropertyInfo, Option)> properties, 
                Type type, 
                Func<IServiceProvider, object> factory)
            {
                _commandAttribute = commandAttribute;
                _arguments = arguments;
                _properties = properties;
                _factory = factory;

                _methodInfo = type.GetMethods().Where((methodInfo) => methodInfo.GetCustomAttribute<CommandInvokeAttribute>() != null).SingleOrDefault() ??
                    throw new ArgumentException($"No command invoke method found in {type}");

                _methodInfoHelp = type.GetMethods().Where((methodInfo) => methodInfo.GetCustomAttribute<HelpInvokeAttribute>() != null).SingleOrDefault();
            }

            Task<int> ICommandHandler.InvokeAsync(InvocationContext context)
            {
                return Task.FromException<int>(new NotImplementedException());
            }

            /// <summary>
            /// Returns the command name
            /// </summary>
            internal string Name => _commandAttribute.Name;

            /// <summary>
            /// Returns the command's help text
            /// </summary>
            internal string Help => _commandAttribute.Help;

            /// <summary>
            /// Returns the list of the command's aliases.
            /// </summary>
            internal IEnumerable<string> Aliases => _commandAttribute.Aliases;

            /// <summary>
            /// Returns true if the command should be added.
            /// </summary>
            internal bool IsValidPlatform(ITarget target)
            {
                if ((_commandAttribute.Platform & CommandPlatform.Global) != 0)
                {
                    return true;
                }
                if (target != null)
                {
                    if (target.OperatingSystem == OSPlatform.Windows)
                    {
                        return (_commandAttribute.Platform & CommandPlatform.Windows) != 0;
                    }
                    if (target.OperatingSystem == OSPlatform.Linux)
                    {
                        return (_commandAttribute.Platform & CommandPlatform.Linux) != 0;
                    }
                    if (target.OperatingSystem == OSPlatform.OSX)
                    {
                        return (_commandAttribute.Platform & CommandPlatform.OSX) != 0;
                    }
                }
                return false;
            }

            /// <summary>
            /// Execute the command synchronously.
            /// </summary>
            /// <param name="context">invocation context</param>
            /// <param name="services">service provider</param>
            internal void Invoke(InvocationContext context, IServiceProvider services) => Invoke(_methodInfo, context, context.Parser, services);

            /// <summary>
            /// Executes the command's help invoke function if exists
            /// </summary>
            /// <param name="parser">parser instance</param>
            /// <param name="services">service provider</param>
            /// <returns>true help called, false no help function</returns>
            internal bool InvokeHelp(Parser parser, IServiceProvider services)
            {
                if (_methodInfoHelp == null) {
                    return false;
                }
                // The InvocationContext is null so the options and arguments in the 
                // command instance created are not set. The context for the command
                // requesting help (either the help command or some other command using
                // --help) won't work for the command instance that implements it's own
                // help (SOS command).
                Invoke(_methodInfoHelp, context: null, parser, services);
                return true;
            }

            private void Invoke(MethodInfo methodInfo, InvocationContext context, Parser parser, IServiceProvider services)
            {
                object instance = _factory(services);
                SetProperties(context, parser, services, instance);

                object[] arguments = BuildArguments(methodInfo, services);
                methodInfo.Invoke(instance, arguments);
            }

            private void SetProperties(InvocationContext context, Parser parser, IServiceProvider services, object instance)
            {
                ParseResult defaultParseResult = null;

                // Parse the default options if any
                string defaultOptions = _commandAttribute.DefaultOptions;
                if (defaultOptions != null) {
                    defaultParseResult = parser.Parse(Name + " " + defaultOptions);
                }

                // Now initialize the option and service properties from the default and command line options
                foreach ((PropertyInfo Property, Option Option) property in _properties)
                {
                    object value = property.Property.GetValue(instance);

                    if (property.Option != null)
                    {
                        if (defaultParseResult != null)
                        {
                            OptionResult defaultOptionResult = defaultParseResult.FindResultFor(property.Option);
                            if (defaultOptionResult != null) {
                                value = defaultOptionResult.GetValueOrDefault();
                            }
                        }
                        if (context != null)
                        {
                            OptionResult optionResult = context.ParseResult.FindResultFor(property.Option);
                            if (optionResult != null) {
                                value = optionResult.GetValueOrDefault();
                            }
                        }
                    }
                    else
                    { 
                        Type propertyType = property.Property.PropertyType;
                        object service = services.GetService(propertyType);
                        if (service != null) {
                            value = service;
                        }
                    }

                    property.Property.SetValue(instance, value);
                }

                // Initialize any argument properties from the default and command line arguments
                foreach ((PropertyInfo Property, Argument Argument) argument in _arguments)
                {
                    object value = argument.Property.GetValue(instance);

                    List<string> array = null;
                    if (argument.Property.PropertyType.IsArray && argument.Property.PropertyType.GetElementType() == typeof(string))
                    {
                        array = new List<string>();
                        if (value is IEnumerable<string> entries) {
                            array.AddRange(entries);
                        }
                    }

                    if (defaultParseResult != null)
                    {
                        ArgumentResult defaultArgumentResult = defaultParseResult.FindResultFor(argument.Argument);
                        if (defaultArgumentResult != null)
                        {
                            value = defaultArgumentResult.GetValueOrDefault();
                            if (array != null && value is IEnumerable<string> entries) {
                                array.AddRange(entries);
                            }
                        }
                    }
                    if (context != null)
                    {
                        ArgumentResult argumentResult = context.ParseResult.FindResultFor(argument.Argument);
                        if (argumentResult != null) 
                        {
                            value = argumentResult.GetValueOrDefault();
                            if (array != null && value is IEnumerable<string> entries) {
                                array.AddRange(entries);
                            }
                        }
                    }

                    argument.Property.SetValue(instance, array != null ? array.ToArray() : value);
                }
            }

            private object[] BuildArguments(MethodBase methodBase, IServiceProvider services)
            {
                ParameterInfo[] parameters = methodBase.GetParameters();
                object[] arguments = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type parameterType = parameters[i].ParameterType;

                    // The parameter will passed as null to allow for "optional" services. The invoked 
                    // method needs to check for possible null parameters.
                    arguments[i] = services.GetService(parameterType);
                }
                return arguments;
            }
        }

        /// <summary>
        /// Local help builder that allows commands to provide more detailed help 
        /// text via the "InvokeHelp" function.
        /// </summary>
        class LocalHelpBuilder : IHelpBuilder
        {
            private readonly CommandService _commandService;
            private readonly IConsole _console;
            private readonly bool _useHelpBuilder;

            public LocalHelpBuilder(CommandService commandService, IConsole console, bool useHelpBuilder)
            {
                _commandService = commandService;
                _console = console;
                _useHelpBuilder = useHelpBuilder;
            }

            void IHelpBuilder.Write(ICommand command)
            {
                bool useHelpBuilder = _useHelpBuilder;
                if (_commandService._commandHandlers.TryGetValue(command.Name, out CommandHandler handler))
                {
                    if (handler.InvokeHelp(_commandService.Parser, LocalConsole.ToServices(_console))) {
                        return;
                    }
                    useHelpBuilder = true;
                }
                if (useHelpBuilder)
                {
                    var helpBuilder = new HelpBuilder(_console, maxWidth: Console.WindowWidth);
                    helpBuilder.Write(command);
                }
            }
        }

        /// <summary>
        /// This class does two things: wraps the IConsoleService and provides the IConsole interface and 
        /// pipes through the System.CommandLine parsing allowing per command invocation data (service 
        /// provider and raw command line) to be passed through.
        /// </summary>
        class LocalConsole : IConsole
        {
            public static IServiceProvider ToServices(IConsole console) => ((LocalConsole)console).Services;

            public readonly IServiceProvider Services;

            private readonly IConsoleService _console;

            public LocalConsole(IServiceProvider services)
            {
                Services = services;
                _console = services.GetService<IConsoleService>();
                Debug.Assert(_console != null);
                Out = new StandardStreamWriter((text) => _console.Write(text));
                Error = new StandardStreamWriter((text) => _console.WriteError(text));
            }

            #region IConsole

            public IStandardStreamWriter Out { get; }

            bool IStandardOut.IsOutputRedirected { get { return false; } }

            public IStandardStreamWriter Error { get; }

            bool IStandardError.IsErrorRedirected { get { return false; } }

            bool IStandardIn.IsInputRedirected { get { return false; } }

            class StandardStreamWriter : IStandardStreamWriter
            {
                readonly Action<string> _write;

                public StandardStreamWriter(Action<string> write) => _write = write;

                void IStandardStreamWriter.Write(string value) => _write(value);
            }

            #endregion
        }
    }
}
