// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Pipes;

namespace DotnetDumpCommands
{
    // This prorgam can be used as a project for testing several dotnet-dump commands (e.g. action parameter in command line arguments)
    internal class Program
    {
        // Every object allocated in this program that should be available in the dump file should be added on this list
        // It ensures those objects are not garbage-collected before taking the dump
        private static readonly List<object> _objectsToKeepInMemory = new List<object>();

        private static string PipeServerName;

        /// <summary>
        /// Application entrypoint
        /// </summary>
        /// <param name="args">
        /// - First argument is the NamedPipe name to connect to, waiting for the dump. This argument is dynamically provided by the test runner
        /// - Second argument is the action to run when invoking this program, so that the program creates the right data set for the test.
        /// This value should be configured in your xUnit test case
        /// </param>
        private static void Main(string[] args)
        {
            if (args.Length <2)
            {
                Console.WriteLine("The provided argument not valid");
                Console.WriteLine("Required parameters: <pipeName> <action>");
                return;
            }

            PipeServerName = args[0];
            Console.WriteLine("Pipe server: {0}", PipeServerName);

            if ("dcd".Equals(args[1]))
            {
                _objectsToKeepInMemory.AddRange(CreateConcurrentDictionaries());
            }
            else if ("dumpgen".Equals(args[1]))
            {
                _objectsToKeepInMemory.AddRange(CreateObjectsInDifferentGenerations());
            }
            else
            {
                Console.WriteLine($"Action parameter {args[1]} is not valid");
                return;
            }

            // Don't delete this following line, it ensures the _objectsToKeepInMemory variable is used and is not thrown away at build time for optimization
            Console.WriteLine($"Number of objects kept in memory = {_objectsToKeepInMemory.Count}");
            WaitForDump();
        }

        private static void WaitForDump()
        {
            if (Program.PipeServerName != null)
            {
                var pipeStream = new NamedPipeClientStream(Program.PipeServerName);
                Console.WriteLine("Connecting to pipe {0}", Program.PipeServerName);
                pipeStream.Connect();

                // Wait for server to send something
                int input = pipeStream.ReadByte();
            }
        }

        private static IEnumerable<object> CreateConcurrentDictionaries()
        {
            var intDictionary = new ConcurrentDictionary<int, int>();
            intDictionary.GetOrAdd(0, 1);
            intDictionary.GetOrAdd(31, 17);
            intDictionary.GetOrAdd(1521482, 512487);

            yield return intDictionary;

            var stringDictionary = new ConcurrentDictionary<string, bool>();
            stringDictionary.GetOrAdd("String true", true);
            stringDictionary.GetOrAdd("String false", false);
            stringDictionary.GetOrAdd(new string('S', 150), false);

            yield return stringDictionary;

            var objDictionary = new ConcurrentDictionary<DumpSampleStruct, DumpSampleClass>();
            objDictionary.GetOrAdd(
                new DumpSampleStruct
                {
                    IntValue = 1,
                    StringValue = "Sample Struct1",
                    Date = DateTime.Now
                },
                new DumpSampleClass
                {
                });
            objDictionary.GetOrAdd(
                new DumpSampleStruct
                {
                    IntValue = 2,
                    StringValue = "Sample Struct2",
                    Date = DateTime.Now
                },
                default(DumpSampleClass));

            yield return objDictionary;

            var structDictionary = new ConcurrentDictionary<int, DumpSampleStruct>();
            structDictionary.GetOrAdd(0, new DumpSampleStruct
            {
                IntValue = 12,
                StringValue = "Sample Struct",
                Date = DateTime.Now
            });

            yield return structDictionary;

            var arrayDictionary = new ConcurrentDictionary<int, string[]>();
            arrayDictionary.GetOrAdd(1, new string[] { "String1", "String2", "String3", "String4" });
            arrayDictionary.GetOrAdd(2, new string[] { "String10", "String20" });

            yield return arrayDictionary;
        }

        private static IEnumerable<object> CreateObjectsInDifferentGenerations()
        {
            // This object should go into LOH
            yield return new DumpSampleClass[50000];

#if NET5_0_OR_GREATER
            // This object should go into POH
            yield return GC.AllocateUninitializedArray<byte>(10000, pinned: true);
#endif

            for (var i = 0; i < 5; i++)
            {
                yield return new DumpSampleClass();
            }
            GC.Collect();

            for (var i = 0; i < 3; i++)
            {
                yield return new DumpSampleClass();
            }
            GC.Collect();

            for (var i = 0; i < 10; i++)
            {
                yield return new DumpSampleClass();
            }
        }

        public class DumpSampleClass
        {
            public bool Value1 { get; set; }
            public string Value2 { get; set; }
            public DateTime Date { get; set; }
        }

        public struct DumpSampleStruct
        {
            public int IntValue;
            public string StringValue;
            public DateTime Date;
        }
    }
}
