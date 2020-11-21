
using System.CommandLine;
using System.CommandLine.Invocation;

namespace DiagnosticsReleaseTool.CommandLine
{
    public static class DiagnosticsReleaseCommandLineUtil
    {
        /// <summary>
        /// Allows the command handler to be included in the collection initializer.
        /// </summary>
        public static void Add(this Command command, ICommandHandler handler)
        {
            command.Handler = handler;
        }
    }
}