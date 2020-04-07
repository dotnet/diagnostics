// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.Repl
{
    /// <summary>
    /// OS Platforms to add command
    /// </summary>
    [Flags]
    public enum CommandPlatform : byte
    {
        All         = 0x00,
        Windows     = 0x01,
        Linux       = 0x02,
        OSX         = 0x04,
    }

    /// <summary>
    /// Base command, option and argument class.
    /// </summary>
    public class BaseAttribute : Attribute
    {
        /// <summary>
        /// Name of the command
        /// </summary>
        public string Name;

        /// <summary>
        /// Displayed in the help for the command
        /// </summary>
        public string Help;
    }

    /// <summary>
    /// Base command and command alias class.
    /// </summary>
    public class CommandBaseAttribute : BaseAttribute
    {
        /// <summary>
        /// Optional OS platform for the command
        /// </summary>
        public CommandPlatform Platform = CommandPlatform.All;
    }

    /// <summary>
    /// Marks the class as a Command.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class CommandAttribute : CommandBaseAttribute
    {
        /// <summary>
        /// Sets the value of the CommandBase.AliasExpansion when the command is executed.
        /// </summary>
        public string AliasExpansion;
    }

    /// <summary>
    /// Adds an alias to the previous command attribute
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class CommandAliasAttribute : CommandBaseAttribute
    {
    }

    /// <summary>
    /// Marks the property as a Option.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class OptionAttribute : BaseAttribute
    {
    }

    /// <summary>
    /// Adds an alias to the Option. Help is ignored.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
    public class OptionAliasAttribute : BaseAttribute
    {
    }

    /// <summary>
    /// Marks the property the command Argument.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class ArgumentAttribute : BaseAttribute
    {
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
