// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.CommandLine;

namespace Microsoft.Tools.Common
{
    public static class CommandExtenions
    {
        public static Command AddOptions(this Command command, params Option[] options)
        {
            foreach (Option option in options)
            {
                command.AddOption(option);
            }
            return command;
        }

        public static Command AddArguments(this Command command, params Argument[] arguments)
        {
            foreach (Argument argument in arguments)
            {
                command.AddArgument(argument);
            }
            return command;
        }
    }
}
