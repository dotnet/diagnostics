// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    class Program
    {
        public static Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
#if DEBUG
                .AddCommand(PortsCommandHandler.ActivePortsCommand())
                .AddCommand(ProvidersCommandHandler.KnownProvidersCommand())
                .AddCommand(StartCommandHandler.StartCommand())
#endif
                .AddCommand(CollectCommandHandler.CollectCommand())
                .AddCommand(StopCommandHandler.StopCommand())
                .UseDefaults()
                .Build();

            return parser.InvokeAsync(args);
        }
    }
}
