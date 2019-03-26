// --------------------------------------------------------------------
// 
// Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// --------------------------------------------------------------------
using System;

namespace Microsoft.Diagnostic.Repl
{
    /// <summary>
    /// Base command option attribute.
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
    /// Marks the class as a Command.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class CommandAttribute : BaseAttribute
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
    public class CommandAliasAttribute : BaseAttribute
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
}
