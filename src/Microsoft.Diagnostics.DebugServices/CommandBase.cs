// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// The common command context
    /// </summary>
    public abstract class CommandBase
    {
        /// <summary>
        /// Console service
        /// </summary>
        public IConsoleService Console { get; set; }

        /// <summary>
        /// Execute the command
        /// </summary>
        [CommandInvoke]
        public abstract void Invoke();

        /// <summary>
        /// Display text
        /// </summary>
        /// <param name="message">text message</param>
        protected void Write(string message)
        {
            Console.Write(message);
        }

        /// <summary>
        /// Display line
        /// </summary>
        /// <param name="message">line message</param>
        protected void WriteLine(string message)
        {
            Console.Write(message + Environment.NewLine);
        }

        /// <summary>
        /// Display formatted text
        /// </summary>
        /// <param name="format">format string</param>
        /// <param name="args">arguments</param>
        protected void WriteLine(string format, params object[] args)
        {
            Console.Write(string.Format(format, args) + Environment.NewLine);
        }

        /// <summary>
        /// Display formatted warning text
        /// </summary>
        /// <param name="format">format string</param>
        /// <param name="args">arguments</param>
        protected void WriteLineWarning(string format, params object[] args)
        {
            Console.WriteWarning(string.Format(format, args) + Environment.NewLine);
        }

        /// <summary>
        /// Display formatted error text
        /// </summary>
        /// <param name="format">format string</param>
        /// <param name="args">arguments</param>
        protected void WriteLineError(string format, params object[] args)
        {
            Console.WriteError(string.Format(format, args) + Environment.NewLine);
        }

        /// <summary>
        /// Convert hexadecimal string address into ulong
        /// </summary>
        /// <param name="addressInHexa">0x12345678 or 000012345670 format are supported</param>
        /// <param name="address">parsed hexadecimal address</param>
        /// <returns></returns>
        protected bool TryParseAddress(string addressInHexa, out ulong address)
        {
            // skip 0x or leading 0000 if needed
            if (addressInHexa.StartsWith("0x"))
                addressInHexa = addressInHexa.Substring(2);
            addressInHexa = addressInHexa.TrimStart('0');

            return ulong.TryParse(addressInHexa, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out address);
        }
    }
}
