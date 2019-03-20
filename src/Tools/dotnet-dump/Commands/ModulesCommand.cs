
using Microsoft.Diagnostic.Repl;
using Microsoft.Diagnostics.Runtime;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostic.Tools.Dump
{
    [Command(Name = "modules", Help = "Displays the native modules in the process.")]
    [CommandAlias(Name = "lm")]
    public class ModulesCommand : CommandBase
    {
        [Option(Name = "--verbose", Help = "Displays more details.")]
        [OptionAlias(Name = "-v")]
        public bool Verbose { get; set; }

        public AnalyzeContext AnalyzeContext { get; set; }

        public override Task InvokeAsync()
        {
            foreach (ModuleInfo module in AnalyzeContext.Target.DataReader.EnumerateModules())
            {
                if (Verbose)
                {
                    WriteLine("{0}", module.FileName);
                    WriteLine("    Address:   {0:X16}", module.ImageBase);
                    WriteLine("    FileSize:  {0:X8}", module.FileSize);
                    WriteLine("    TimeStamp: {0:X8}", module.TimeStamp);
                    if (module.BuildId != null) {
                        WriteLine("    BuildId:   {0}", string.Concat(module.BuildId.Select((b) => b.ToString("x2"))));
                    }
                    WriteLine("    IsRuntime: {0}", module.IsRuntime);
                    WriteLine("    IsManaged: {0}", module.IsManaged);
                }
                else
                {
                    WriteLine("{0:X16} {1:X8} {2}", module.ImageBase, module.FileSize, module.FileName);
                }
            }
            return Task.CompletedTask;
        }
    }
}
