// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Command flags to filter by OS Platforms, control scope and how the command is registered.
    /// </summary>
    [Flags]
    public enum CommandFlags : byte
    {
        Windows = 0x01,
        Linux = 0x02,
        OSX = 0x04,

        /// <summary>
        /// Command is supported when there is no target
        /// </summary>
        Global = 0x08,

        /// <summary>
        /// Command is not added through reflection, but manually with command service API.
        /// </summary>
        Manual = 0x10,

        /// <summary>
        /// Default. All operating system, but target is required
        /// </summary>
        Default = Windows | Linux | OSX
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
        /// Command flags to filter by OS Platforms, control scope and how the command is registered.
        /// </summary>
        public CommandFlags Flags = CommandFlags.Default;

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
    /// Marks a class as a helper command.  These commands are helpers to make other commands in
    /// SOS work.  They aren't meant to be used generally as a command.  It's fine for anyone to
    /// call these commands, but they aren't discoverable via !help.
    /// </summary>
    public class HelperCommandAttribute : CommandAttribute
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
    /// Marks the function to invoke to display alternate help for command.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class HelpInvokeAttribute : Attribute
    {
    }
}
