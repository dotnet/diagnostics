// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Marks the class as a Command.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class CommandAttribute : Attribute
    {
        /// <summary>
        /// Name of the command
        /// </summary>
        public string Name;

        /// <summary>
        /// Displayed in the help for the command and any aliases
        /// </summary>
        public string Help;

        /// <summary>
        /// The command's aliases
        /// </summary>
        public string[] Aliases = Array.Empty<string>();

        /// <summary>
        /// A string of options that are parsed before the command line options
        /// </summary>
        public string DefaultOptions;
    }

    /// <summary>
    /// Marks a class as a Debug-Only command.  These commands are only available for debug versions
    /// of SOS, but does not appear in shipping builds.
    /// </summary>
    [Conditional("DEBUG")]
    public class DebugCommandAttribute : CommandAttribute
    {
    }

    /// <summary>
    /// Marks the property as a Option.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class OptionAttribute : Attribute
    {
        /// <summary>
        /// Name of the option i.e "--pid"
        /// </summary>
        public string Name;

        /// <summary>
        /// Displayed in the help for the option and any aliases
        /// </summary>
        public string Help;

        /// <summary>
        /// The option's aliases i.e "-p"
        /// </summary>
        public string[] Aliases = Array.Empty<string>();
    }

    /// <summary>
    /// Marks the property the command Argument.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class ArgumentAttribute : Attribute
    {
        /// <summary>
        /// Name of the argument
        /// </summary>
        public string Name;

        /// <summary>
        /// Displayed in the help for the argument
        /// </summary>
        public string Help;
    }

    /// <summary>
    /// Marks the function to invoke to execute the command.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class CommandInvokeAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks the function to invoke to return the alternate help for command. The function returns
    /// a string. The Argument and Option properties of the command are not set.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class HelpInvokeAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks the function to invoke to filter a command. The function returns a bool; true if
    /// the command is supported. The Argument and Option properties of the command are not set.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class FilterInvokeAttribute : Attribute
    {
        /// <summary>
        /// Message to display if the filter fails
        /// </summary>
        public string Message;
    }
}
