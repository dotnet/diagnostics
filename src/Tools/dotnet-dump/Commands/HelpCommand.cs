using Microsoft.Diagnostic.Repl;
using System.CommandLine;
using System.Threading.Tasks;

namespace Microsoft.Diagnostic.Tools.Dump
{
    [Command(Name = "help", Help = "Display help for a command.")]
    public class HelpCommand : CommandBase
    {
        [Argument(Help = "Command to find help.")]
        public string Command { get; set; }

        public CommandProcessor CommandProcessor { get; set; }

        public override Task InvokeAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Get help builder interface
        /// </summary>
        /// <param name="helpBuilder">help builder</param>
        public Task InvokeAsync(IHelpBuilder helpBuilder)
        {
            Command command = CommandProcessor.GetCommand(Command);
            if (command != null) {
                helpBuilder.Write(command);
            }
            else {
                Console.Error.WriteLine($"Help for {Command} not found.");
            }
            return Task.CompletedTask;
        }
    }
}
