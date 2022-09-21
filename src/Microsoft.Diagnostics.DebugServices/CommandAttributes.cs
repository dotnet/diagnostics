// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// OS Platforms to add command
    /// </summary>
    [Flags]
    public enum CommandPlatform : byte
    {
        Windows     = 0x01,
        Linux       = 0x02,
        OSX         = 0x04,

        /// <summary>
        /// Command is supported when there is no target
        /// </summary>
        Global      = 0x08,

        /// <summary>
        /// Default. All operating system, but target is required
        /// </summary>
        Default     = Windows | Linux | OSX
    }

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
        /// Optional OS platform for the command
        /// </summary>
        public CommandPlatform Platform = CommandPlatform.Default;

        /// <summary>
        /// A string of options that are parsed before the command line options
        /// </summary>
        public string DefaultOptions;
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
    /// Marks the function to invoke to display alternate help for command.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class HelpInvokeAttribute : Attribute
    {
    }
}
