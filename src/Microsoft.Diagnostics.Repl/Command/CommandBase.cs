// --------------------------------------------------------------------
// 
// Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// --------------------------------------------------------------------
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Repl
{
    /// <summary>
    /// The common command context
    /// </summary>
    public abstract class CommandBase
    {
        /// <summary>
        /// Parser invocation context. Contains the ParseResult, CommandResult, etc. Is null when 
        /// InvokeAdditionalHelp is called.
        /// </summary>
        public InvocationContext InvocationContext { get; set; }

        /// <summary>
        /// Console instance
        /// </summary>
        public IConsole Console { get; set; }

        /// <summary>
        /// The AliasExpansion value from the CommandAttribute or null if none.
        /// </summary>
        public string AliasExpansion { get; set; }

        /// <summary>
        /// Execute the command
        /// </summary>
        [CommandInvoke]
        public abstract void Invoke();

        /// <summary>
        /// Display text
        /// </summary>
        /// <param name="message">text message</param>
        protected void WriteLine(string message)
        {
            Console.Out.WriteLine(message);
        }

        /// <summary>
        /// Display formatted text
        /// </summary>
        /// <param name="format">format string</param>
        /// <param name="args">arguments</param>
        protected void WriteLine(string format, params object[] args)
        {
            Console.Out.WriteLine(string.Format(format, args));
        }
    }
}