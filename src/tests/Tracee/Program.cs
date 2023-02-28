// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO.Pipes;

namespace Tracee
{
    static class Program
    {
        public static int Main(string[] args)
        {
            int pid = Environment.ProcessId;
            string pipeServerName = args.Length > 0 ? args[0] : null;
            if (pipeServerName == null) 
            {
                Console.Error.WriteLine($"{pid} Tracee: no pipe name");
                Console.Error.Flush();
                return -1;
            }
            Console.WriteLine($"{pid} Tracee: pipe server: {pipeServerName}");
            Console.Out.Flush();
            try
            {
                using var pipeStream = new NamedPipeClientStream(pipeServerName);

                Console.WriteLine("{0} Tracee: connecting to pipe", pid);
                Console.Out.Flush();
                pipeStream.Connect(5 * 60 * 1000);
                Console.WriteLine("{0} Tracee: connected to pipe", pid);
                Console.Out.Flush();

                // Wait for server to send something
                int input = pipeStream.ReadByte();

                Console.WriteLine("{0} Tracee: waking up {1}", pid, input);
                Console.Out.Flush();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                Console.Error.Flush();
                return -1;
            }
            Console.WriteLine("{0} Tracee: exiting normally", pid);
            Console.Out.Flush();
            return 0;
        }
    }
}
