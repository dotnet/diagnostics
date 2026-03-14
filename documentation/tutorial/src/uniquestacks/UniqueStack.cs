using Microsoft.Diagnostic.Repl;
using Microsoft.Diagnostics.Runtime;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostic.Tools.Dump
{
    internal class Stack
    {
        public List<uint> threadIds = new List<uint>();
    }


    [Command(Name = "uniquestacks", Help = "Displays the unique managed stacks.")]
    [CommandAlias(Name = "us")]
    public class UniqueStacksCommand : CommandBase
    {
        [Option(Name = "--verbose", Help = "Displays more details.")]
        [OptionAlias(Name = "-v")]
        public bool Verbose { get; set; }

        public AnalyzeContext AnalyzeContext { get; set; }

        public override Task InvokeAsync()
        {
            Dictionary<string, Stack> threads = new Dictionary<string, Stack>();
   
            foreach (ClrThread thread in AnalyzeContext.Runtime.Threads)
            {
                if (!thread.IsAlive) continue;

                StringBuilder completeStack = new StringBuilder();
                foreach (ClrStackFrame frame in thread.StackTrace)
                {
                    //Console.WriteLine("{0,16:X} {1,16:X} {2}", frame.StackPointer, frame.InstructionPointer, frame.DisplayString);
                    completeStack.Append(frame.StackPointer + " " + frame.InstructionPointer + " " + frame.DisplayString + "\n");
                }

                string cStack = completeStack.ToString();

                if(threads.ContainsKey(cStack)==true)
                {
                    threads[cStack].threadIds.Add(thread.OSThreadId);
                }
                else
                {
                    Stack s = new Stack();
                    s.threadIds.Add(thread.OSThreadId);
                    threads.Add(cStack, s);
                }
            }

            foreach (KeyValuePair<string, Stack> item in threads)
            {
                System.Console.WriteLine(item.Key);
                System.Console.WriteLine("\n\n");
            }

            return Task.CompletedTask;
        }
    }
}