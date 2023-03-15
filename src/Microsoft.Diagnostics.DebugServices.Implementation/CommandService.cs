// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Implements the ICommandService interface using System.CommandLine.
    /// </summary>
    public class CommandService : ICommandService
    {
        private readonly List<CommandGroup> _commandGroups = new();
        private readonly string _commandPrompt;

        /// <summary>
        /// Create an instance of the command processor;
        /// </summary>
        /// <param name="commandPrompt">command prompted used in help message</param>
        public CommandService(string commandPrompt = null)
        {
            _commandPrompt = commandPrompt ?? ">";

            // Create default command group (should always be last in this list)
            _commandGroups.Add(new CommandGroup(_commandPrompt));
        }

        /// <summary>
        /// Parse and execute the command line.
        /// </summary>
        /// <param name="commandLine">command line text</param>
        /// <param name="services">services for the command</param>
        /// <returns>true - found command, false - command not found</returns>
        /// <exception cref="ArgumentException">empty command line</exception>
        /// <exception cref="DiagnosticsException">other errors</exception>
        /// <exception cref="CommandParsingException ">parsing error</exception>
        public bool Execute(string commandLine, IServiceProvider services)
        {
            string[] commandLineArray = CommandLineStringSplitter.Instance.Split(commandLine).ToArray();
            if (commandLineArray.Length <= 0)
            {
                throw new ArgumentException("Empty command line", nameof(commandLine));
            }
            string commandName = commandLineArray[0].Trim();
            return Execute(commandName, commandLineArray, services);
        }

        /// <summary>
        /// Parse and execute the command.
        /// </summary>
        /// <param name="commandName">command name</param>
        /// <param name="commandArguments">command arguments/options</param>
        /// <param name="services">services for the command</param>
        /// <returns>true - found command, false - command not found</returns>
        /// <exception cref="ArgumentException">empty command name or arguments</exception>
        /// <exception cref="DiagnosticsException">other errors</exception>
        /// <exception cref="CommandParsingException ">parsing error</exception>
        public bool Execute(string commandName, string commandArguments, IServiceProvider services)
        {
            commandName = commandName.Trim();
            string[] commandLineArray = CommandLineStringSplitter.Instance.Split(commandName + " " + (commandArguments ?? "")).ToArray();
            if (commandLineArray.Length <= 0)
            {
                throw new ArgumentException("Empty command name or arguments", nameof(commandArguments));
            }
            return Execute(commandName, commandLineArray, services);
        }

        /// <summary>
        /// Find, parse and execute the command.
        /// </summary>
        /// <param name="commandName">command name</param>
        /// <param name="commandLineArray">command line</param>
        /// <param name="services">services for the command</param>
        /// <returns>true - found command, false - command not found</returns>
        /// <exception cref="ArgumentException">empty command name</exception>
        /// <exception cref="DiagnosticsException">other errors</exception>
        /// <exception cref="CommandParsingException ">parsing error</exception>
        private bool Execute(string commandName, string[] commandLineArray, IServiceProvider services)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                throw new ArgumentException("Empty command name", nameof(commandName));
            }
            List<string> messages = new();
            foreach (CommandGroup group in _commandGroups)
            {
                if (group.TryGetCommandHandler(commandName, out CommandHandler handler))
                {
                    try
                    {
                        if (handler.IsCommandSupported(group.Parser, services))
                        {
                            if (group.Execute(commandLineArray, services))
                            {
                                return true;
                            }
                        }
                        if (handler.FilterInvokeMessage != null)
                        {
                            messages.Add(handler.FilterInvokeMessage);
                        }
                    }
                    catch (CommandNotFoundException ex)
                    {
                        messages.Add(ex.Message);
                    }
                }
            }
            if (messages.Count > 0)
            {
                throw new CommandNotFoundException(string.Concat(messages.Select(s => s + Environment.NewLine)));
            }
            return false;
        }

        /// <summary>
        /// Displays the help for a command
        /// </summary>
        /// <param name="services">service provider</param>
        /// <returns>command invocation and help enumeration</returns>
        public IEnumerable<(string Invocation, string Help)> GetHelp(IServiceProvider services)
        {
            List<(string Invocation, string Help)> help = new();
            foreach (CommandGroup group in _commandGroups)
            {
                foreach (CommandHandler handler in group.CommandHandlers)
                {
                    try
                    {
                        if (handler.IsCommandSupported(group.Parser, services))
                        {
                            string invocation = handler.HelpInvocation;
                            help.Add((invocation, handler.Help));
                        }
                    }
                    catch (CommandNotFoundException)
                    {
                    }
                }
            }
            return help;
        }

        /// <summary>
        /// Displays the detailed help for a command
        /// </summary>
        /// <param name="commandName">name of the command or alias</param>
        /// <param name="services">service provider</param>
        /// <param name="consoleWidth">the width to format the help or int.MaxValue</param>
        /// <returns>help text or null if not found</returns>
        public string GetDetailedHelp(string commandName, IServiceProvider services, int consoleWidth)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                throw new ArgumentNullException(nameof(commandName));
            }
            List<string> messages = new();
            foreach (CommandGroup group in _commandGroups)
            {
                if (group.TryGetCommand(commandName, out Command command))
                {
                    if (command.Handler is CommandHandler handler)
                    {
                        try
                        {
                            if (handler.IsCommandSupported(group.Parser, services))
                            {
                                return group.GetDetailedHelp(command, services, consoleWidth);
                            }
                            if (handler.FilterInvokeMessage != null)
                            {
                                messages.Add(handler.FilterInvokeMessage);
                            }
                        }
                        catch (CommandNotFoundException ex)
                        {
                            messages.Add(ex.Message);
                        }
                    }
                }
            }
            if (messages.Count > 0)
            {
                return string.Concat(messages.Select(s => s + Environment.NewLine));
            }
            return null;
        }

        /// <summary>
        /// Enumerates all the command's name, help and aliases
        /// </summary>
        public IEnumerable<(string name, string help, IEnumerable<string> aliases)> Commands =>
            _commandGroups.SelectMany((group) => group.CommandHandlers).Select((handler) => (handler.Name, handler.Help, handler.Aliases));

        /// <summary>
        /// Add the commands and aliases attributes found in the type.
        /// </summary>
        /// <param name="type">Command type to search</param>
        public void AddCommands(Type type) => AddCommands(type, factory: null);

        /// <summary>
        /// Add the commands and aliases attributes found in the type.
        /// </summary>
        /// <param name="type">Command type to search</param>
        /// <param name="factory">function to create command instance</param>
        public void AddCommands(Type type, Func<IServiceProvider, object> factory)
        {
            if (type.IsClass)
            {
                for (Type baseType = type; baseType != null; baseType = baseType.BaseType)
                {
                    if (baseType == typeof(CommandBase))
                    {
                        break;
                    }
                    CommandAttribute[] commandAttributes = (CommandAttribute[])baseType.GetCustomAttributes(typeof(CommandAttribute), inherit: true);
                    foreach (CommandAttribute commandAttribute in commandAttributes)
                    {
                        factory ??= (services) => Utilities.CreateInstance(type, services);

                        bool dup = true;
                        foreach (CommandGroup group in _commandGroups)
                        {
                            // If the group doesn't contain a duplicate command name, add it to that group
                            if (!group.Contains(commandAttribute.Name))
                            {
                                group.CreateCommand(baseType, commandAttribute, factory);
                                dup = false;
                                break;
                            }
                        }
                        // If this is a duplicate command, create a new group and add it to the beginning. The default group must be last.
                        if (dup)
                        {
                            CommandGroup group = new(_commandPrompt);
                            _commandGroups.Insert(0, group);
                            group.CreateCommand(baseType, commandAttribute, factory);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// This groups like commands that may have the same name as another group or the default one.
        /// </summary>
        private sealed class CommandGroup
        {
            private Parser _parser;
            private readonly CommandLineBuilder _rootBuilder;
            private readonly Dictionary<string, CommandHandler> _commandHandlers = new();

            /// <summary>
            /// Create an instance of the command processor;
            /// </summary>
            /// <param name="commandPrompt">command prompted used in help message</param>
            public CommandGroup(string commandPrompt = null)
            {
                _rootBuilder = new CommandLineBuilder(new Command(commandPrompt));
            }

            /// <summary>
            /// Parse and execute the command line.
            /// </summary>
            /// <param name="commandLine">command line text</param>
            /// <param name="services">services for the command</param>
            /// <returns>true if command was found and executed without error</returns>
            /// <exception cref="DiagnosticsException">parsing error</exception>
            internal bool Execute(IReadOnlyList<string> commandLine, IServiceProvider services)
            {
                // Parse the command line and invoke the command
                ParseResult parseResult = Parser.Parse(commandLine);

                if (parseResult.Errors.Count > 0)
                {
                    StringBuilder sb = new();
                    foreach (ParseError error in parseResult.Errors)
                    {
                        sb.AppendLine(error.Message);
                    }
                    string helpText = GetDetailedHelp(parseResult.CommandResult.Command, services, int.MaxValue);
                    throw new CommandParsingException(sb.ToString(), helpText);
                }
                else
                {
                    if (parseResult.CommandResult.Command is Command command)
                    {
                        if (command.Handler is CommandHandler handler)
                        {
                            InvocationContext context = new(parseResult, new LocalConsole(services.GetService<IConsoleService>()));
                            handler.Invoke(context, services);
                            return true;
                        }
                    }
                }
                return false;
            }

            /// <summary>
            /// Build/return parser
            /// </summary>
            internal Parser Parser => _parser ??= _rootBuilder.Build();

            /// <summary>
            /// Returns all the command handler instances
            /// </summary>
            internal IEnumerable<CommandHandler> CommandHandlers => _commandHandlers.Values;

            /// <summary>
            /// Returns true if command or command alias is found
            /// </summary>
            internal bool Contains(string commandName) => _rootBuilder.Command.Children.Contains(commandName);

            /// <summary>
            /// Returns the command handler for the command or command alias
            /// </summary>
            /// <param name="commandName">command or alias</param>
            /// <param name="handler">handler instance</param>
            /// <returns>true if found</returns>
            internal bool TryGetCommandHandler(string commandName, out CommandHandler handler)
            {
                handler = null;
                if (TryGetCommand(commandName, out Command command))
                {
                    handler = command.Handler as CommandHandler;
                }
                return handler != null;
            }

            /// <summary>
            /// Returns the command instance for the command or command alias
            /// </summary>
            /// <param name="commandName">command or alias</param>
            /// <param name="command">command instance</param>
            /// <returns>true if found</returns>
            internal bool TryGetCommand(string commandName, out Command command)
            {
                command = _rootBuilder.Command.Children.GetByAlias(commandName) as Command;
                return command != null;
            }

            /// <summary>
            /// Add the commands and aliases attributes found in the type.
            /// </summary>
            /// <param name="type">Command type to search</param>
            /// <param name="factory">function to create command instance</param>
            internal void AddCommands(Type type, Func<IServiceProvider, object> factory)
            {
                for (Type baseType = type; baseType != null; baseType = baseType.BaseType)
                {
                    if (baseType == typeof(CommandBase))
                    {
                        break;
                    }
                    CommandAttribute[] commandAttributes = (CommandAttribute[])baseType.GetCustomAttributes(typeof(CommandAttribute), inherit: false);
                    foreach (CommandAttribute commandAttribute in commandAttributes)
                    {
                        factory ??= (services) => Utilities.CreateInstance(type, services);
                        CreateCommand(baseType, commandAttribute, factory);
                    }
                }

                // Build or re-build parser instance after all these commands and aliases are added
                FlushParser();
            }

            internal void CreateCommand(Type type, CommandAttribute commandAttribute, Func<IServiceProvider, object> factory)
            {
                Command command = new(commandAttribute.Name, commandAttribute.Help);
                List<(PropertyInfo, Argument)> arguments = new();
                List<(PropertyInfo, Option)> options = new();

                foreach (string alias in commandAttribute.Aliases)
                {
                    command.AddAlias(alias);
                }

                foreach (PropertyInfo property in type.GetProperties().Where(p => p.CanWrite))
                {
                    ArgumentAttribute argumentAttribute = (ArgumentAttribute)property.GetCustomAttributes(typeof(ArgumentAttribute), inherit: false).SingleOrDefault();
                    if (argumentAttribute != null)
                    {
                        IArgumentArity arity = property.PropertyType.IsArray ? ArgumentArity.ZeroOrMore : ArgumentArity.ZeroOrOne;

                        Argument argument = new()
                        {
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
                        OptionAttribute optionAttribute = (OptionAttribute)property.GetCustomAttributes(typeof(OptionAttribute), inherit: false).SingleOrDefault();
                        if (optionAttribute != null)
                        {
                            Option option = new(optionAttribute.Name ?? BuildOptionAlias(property.Name), optionAttribute.Help)
                            {
                                Argument = new Argument { ArgumentType = property.PropertyType }
                            };
                            command.AddOption(option);
                            options.Add((property, option));

                            foreach (string alias in optionAttribute.Aliases)
                            {
                                option.AddAlias(alias);
                            }
                        }
                    }
                }

                CommandHandler handler = new(commandAttribute, arguments, options, type, factory);
                _commandHandlers.Add(command.Name, handler);
                command.Handler = handler;
                _rootBuilder.AddCommand(command);

                // Build or re-build parser instance after this command is added
                FlushParser();
            }

            internal string GetDetailedHelp(ICommand command, IServiceProvider services, int windowWidth)
            {
                CaptureConsole console = new();

                // Get the command help
                HelpBuilder helpBuilder = new(console, maxWidth: windowWidth);
                helpBuilder.Write(command);

                // Get the detailed help if any
                if (TryGetCommandHandler(command.Name, out CommandHandler handler))
                {
                    string helpText = handler.GetDetailedHelp(Parser, services);
                    if (helpText is not null)
                    {
                        console.Out.Write(helpText);
                    }
                }

                return console.ToString();
            }

            private void FlushParser() => _parser = null;

            private static string BuildOptionAlias(string parameterName)
            {
                if (string.IsNullOrWhiteSpace(parameterName))
                {
                    throw new ArgumentException("Value cannot be null or whitespace.", nameof(parameterName));
                }
                return parameterName.Length > 1 ? $"--{parameterName.ToKebabCase()}" : $"-{parameterName.ToLowerInvariant()}";
            }
        }

        /// <summary>
        /// The normal command handler.
        /// </summary>
        private sealed class CommandHandler : ICommandHandler
        {
            private readonly CommandAttribute _commandAttribute;
            private readonly IEnumerable<(PropertyInfo Property, Argument Argument)> _arguments;
            private readonly IEnumerable<(PropertyInfo Property, Option Option)> _options;

            private readonly Func<IServiceProvider, object> _factory;
            private readonly MethodInfo _methodInfo;
            private readonly MethodInfo _methodInfoHelp;
            private readonly MethodInfo _methodInfoFilter;
            private readonly FilterInvokeAttribute _filterInvokeAttribute;

            public CommandHandler(
                CommandAttribute commandAttribute,
                IEnumerable<(PropertyInfo, Argument)> arguments,
                IEnumerable<(PropertyInfo, Option)> options,
                Type type,
                Func<IServiceProvider, object> factory)
            {
                _commandAttribute = commandAttribute;
                _arguments = arguments;
                _options = options;
                _factory = factory;

                // Now search for the command, help and filter attributes in the command type
                foreach (MethodInfo methodInfo in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy))
                {
                    if (methodInfo.GetCustomAttribute<CommandInvokeAttribute>() != null)
                    {
                        if (_methodInfo != null)
                        {
                            throw new ArgumentException($"Multiple CommandInvokeAttribute's found in {type}");
                        }
                        _methodInfo = methodInfo;
                    }
                    if (methodInfo.GetCustomAttribute<HelpInvokeAttribute>() != null)
                    {
                        if (_methodInfoHelp != null)
                        {
                            throw new ArgumentException($"Multiple HelpInvokeAttribute's found in {type}");
                        }
                        if (methodInfo.ReturnType != typeof(string))
                        {
                            throw new ArgumentException($"HelpInvokeAttribute doesn't return string in {type}");
                        }
                        _methodInfoHelp = methodInfo;
                    }
                    FilterInvokeAttribute filterInvokeAttribute = methodInfo.GetCustomAttribute<FilterInvokeAttribute>();
                    if (filterInvokeAttribute != null)
                    {
                        if (_methodInfoFilter != null)
                        {
                            throw new ArgumentException($"Multiple FilterInvokeAttribute's found in {type}");
                        }
                        if (methodInfo.ReturnType != typeof(bool))
                        {
                            throw new ArgumentException($"FilterInvokeAttribute doesn't return bool in {type}");
                        }
                        _filterInvokeAttribute = filterInvokeAttribute;
                        _methodInfoFilter = methodInfo;
                    }
                }
                if (_methodInfo == null)
                {
                    throw new ArgumentException($"No command invoke method found in {type}");
                }
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
            /// Filter invoke message or null if no attribute or message
            /// </summary>
            internal string FilterInvokeMessage => _filterInvokeAttribute?.Message;

            /// <summary>
            /// Returns the list of the command's aliases.
            /// </summary>
            internal IEnumerable<string> Aliases => _commandAttribute.Aliases;

            /// <summary>
            /// Returns the list of arguments
            /// </summary>
            internal IEnumerable<Argument> Arguments => _arguments.Select((a) => a.Argument);

            /// <summary>
            /// Returns true is the command is supported by the command filter. Calls the FilterInvokeAttribute marked method.
            /// </summary>
            internal bool IsCommandSupported(Parser parser, IServiceProvider services) => _methodInfoFilter == null || (bool)Invoke(_methodInfoFilter, context: null, parser, services);

            /// <summary>
            /// Execute the command synchronously.
            /// </summary>
            /// <param name="context">invocation context</param>
            /// <param name="services">service provider</param>
            internal void Invoke(InvocationContext context, IServiceProvider services) => Invoke(_methodInfo, context, context.Parser, services);

            /// <summary>
            /// Return the various ways the command can be invoked. For building the help text.
            /// </summary>
            internal string HelpInvocation
            {
                get
                {
                    IEnumerable<string> rawAliases = new string[] { Name }.Concat(Aliases);
                    string invocation = string.Join(", ", rawAliases);
                    foreach (Argument argument in Arguments)
                    {
                        string argumentDescriptor = argument.Name;
                        if (!string.IsNullOrWhiteSpace(argumentDescriptor))
                        {
                            invocation = $"{invocation} <{argumentDescriptor}>";
                        }
                    }
                    return invocation;
                }
            }

            /// <summary>
            /// Executes the command's help invoke function if exists
            /// </summary>
            /// <param name="parser">parser instance</param>
            /// <param name="services">service provider</param>
            /// <returns>true help called, false no help function</returns>
            internal string GetDetailedHelp(Parser parser, IServiceProvider services)
            {
                if (_methodInfoHelp == null)
                {
                    return null;
                }
                // The InvocationContext is null so the options and arguments in the
                // command instance created are not set. The context for the command
                // requesting help (either the help command or some other command using
                // --help) won't work for the command instance that implements it's own
                // help (SOS command).
                return (string)Invoke(_methodInfoHelp, context: null, parser, services);
            }

            private object Invoke(MethodInfo methodInfo, InvocationContext context, Parser parser, IServiceProvider services)
            {
                object instance = null;
                if (!methodInfo.IsStatic)
                {
                    instance = _factory(services);
                    SetProperties(context, parser, instance);
                }
                return Utilities.Invoke(methodInfo, instance, services);
            }

            private void SetProperties(InvocationContext context, Parser parser, object instance)
            {
                ParseResult defaultParseResult = null;

                // Parse the default options if any
                string defaultOptions = _commandAttribute.DefaultOptions;
                if (defaultOptions != null)
                {
                    defaultParseResult = parser.Parse(Name + " " + defaultOptions);
                }

                // Now initialize the option and service properties from the default and command line options
                foreach ((PropertyInfo Property, Option Option) option in _options)
                {
                    object value = option.Property.GetValue(instance);

                    if (defaultParseResult != null)
                    {
                        OptionResult defaultOptionResult = defaultParseResult.FindResultFor(option.Option);
                        if (defaultOptionResult != null)
                        {
                            value = defaultOptionResult.GetValueOrDefault();
                        }
                    }
                    if (context != null)
                    {
                        OptionResult optionResult = context.ParseResult.FindResultFor(option.Option);
                        if (optionResult != null)
                        {
                            value = optionResult.GetValueOrDefault();
                        }
                    }

                    option.Property.SetValue(instance, value);
                }

                // Initialize any argument properties from the default and command line arguments
                foreach ((PropertyInfo Property, Argument Argument) argument in _arguments)
                {
                    object value = argument.Property.GetValue(instance);

                    List<string> array = null;
                    if (argument.Property.PropertyType.IsArray && argument.Property.PropertyType.GetElementType() == typeof(string))
                    {
                        array = new List<string>();
                        if (value is IEnumerable<string> entries)
                        {
                            array.AddRange(entries);
                        }
                    }

                    if (defaultParseResult != null)
                    {
                        ArgumentResult defaultArgumentResult = defaultParseResult.FindResultFor(argument.Argument);
                        if (defaultArgumentResult != null)
                        {
                            value = defaultArgumentResult.GetValueOrDefault();
                            if (array != null && value is IEnumerable<string> entries)
                            {
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
                            if (array != null && value is IEnumerable<string> entries)
                            {
                                array.AddRange(entries);
                            }
                        }
                    }

                    argument.Property.SetValue(instance, array != null ? array.ToArray() : value);
                }
            }
        }

        /// <summary>
        /// IConsole implementation that captures all the output into a string.
        /// </summary>
        private sealed class CaptureConsole : IConsole
        {
            private readonly StringBuilder _builder = new();

            public CaptureConsole()
            {
                Out = Error = new StandardStreamWriter((text) => _builder.Append(text));
            }

            public override string ToString() => _builder.ToString();

            #region IConsole

            public IStandardStreamWriter Out { get; }

            bool IStandardOut.IsOutputRedirected { get { return false; } }

            public IStandardStreamWriter Error { get; }

            bool IStandardError.IsErrorRedirected { get { return false; } }

            bool IStandardIn.IsInputRedirected { get { return false; } }

            #endregion
        }

        /// <summary>
        /// This class wraps the IConsoleService and provides the IConsole interface for System.CommandLine.
        /// </summary>
        private sealed class LocalConsole : IConsole
        {
            private readonly IConsoleService _consoleService;

            public LocalConsole(IConsoleService consoleService)
            {
                _consoleService = consoleService;
                Out = new StandardStreamWriter(_consoleService.Write);
                Error = new StandardStreamWriter(_consoleService.WriteError);
            }

            #region IConsole

            public IStandardStreamWriter Out { get; }

            bool IStandardOut.IsOutputRedirected { get { return false; } }

            public IStandardStreamWriter Error { get; }

            bool IStandardError.IsErrorRedirected { get { return false; } }

            bool IStandardIn.IsInputRedirected { get { return false; } }

            #endregion
        }

        private sealed class StandardStreamWriter : IStandardStreamWriter
        {
            private readonly Action<string> _write;

            public StandardStreamWriter(Action<string> write) => _write = write;

            void IStandardStreamWriter.Write(string value) => _write(value);
        }
    }
}
