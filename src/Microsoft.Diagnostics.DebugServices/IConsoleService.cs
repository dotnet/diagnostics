// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Console output service
    /// </summary>
    public interface IConsoleService
    {
        /// <summary>
        /// Write text to console's standard out
        /// </summary>
        /// <param name="value">text</param>
        void Write(string value);

        /// <summary>
        /// Write text to console's standard error
        /// </summary>
        /// <param name="value"></param>
        void WriteError(string value);

        /// <summary>
        /// Exit the interactive console 
        /// </summary>
        void Exit();
    }
}