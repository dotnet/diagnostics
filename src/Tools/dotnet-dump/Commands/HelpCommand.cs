using Microsoft.Diagnostic.Repl;
using System.CommandLine;

namespace Microsoft.Diagnostic.Tools.Dump
{
    [Command(Name = "help", Help = "Display help for a command.")]
    public class HelpCommand : CommandBase
    {
        [Argument(Help = "Command to find help.")]
        public string Command { get; set; }

        public CommandProcessor CommandProcessor { get; set; }

        public IHelpBuilder HelpBuilder { get; set; }

        public override void Invoke()
        {
            Command command = CommandProcessor.GetCommand(Command);
            if (command != null) {
                HelpBuilder.Write(command);
            }
            else {
                Console.Error.WriteLine($"Help for {Command} not found.");
            }
        }
    }
}
