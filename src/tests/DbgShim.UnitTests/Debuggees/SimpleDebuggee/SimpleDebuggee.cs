using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;

class Simple
{
    public static int Main(string[] args)
    {
        string pipeServerName = args[0];
        int pid = Process.GetCurrentProcess().Id;
        Console.WriteLine("{0} SimpleDebuggee: pipe server: {1}", pid, pipeServerName);
        Console.Out.Flush();

        if (pipeServerName != null)
        {
            try
            {
                using var pipeStream = new NamedPipeClientStream(pipeServerName);

                Console.WriteLine("{0} SimpleDebuggee: connecting to pipe", pid);
                Console.Out.Flush();
                pipeStream.Connect((int)TimeSpan.FromMinutes(5).TotalMilliseconds);

                Console.WriteLine("{0} SimpleDebuggee: connected to pipe", pid);
                Console.Out.Flush();

                // Wait for server to send something
                int input = pipeStream.ReadByte();

                Console.WriteLine("{0} SimpleDebuggee: waking up {1}", pid, input);
                Console.Out.Flush();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                Console.Error.Flush();
            }
        }
        return 0;
    }
}
