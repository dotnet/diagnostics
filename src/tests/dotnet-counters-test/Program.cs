using System;
using System.Diagnostics;

namespace dotnet_counters_test
{
    class Program
    {
        static void Main(string[] args)
        {
            RunCollectTests();

            Console.WriteLine("Test passed.");
        }


        /// <summary>
        /// Run tests to verify collect command and the result files
        /// </summary>
        static void RunCollectTests()
        {
            Console.WriteLine("Running collect tests");

            Process targetProcess = new Process();
            

        }
    }
}
