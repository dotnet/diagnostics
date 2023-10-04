// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;

namespace TestExtension
{
    [Command(Name = "clrstack", Help = "Test command #1")]
    public class TestCommand1 : CommandBase
    {
        [ServiceImport]
        public ITarget Target { get; set; }

        [Argument(Name = "FileName", Help = "Test argument.")]
        public string FileName { get; set; }

        public override void Invoke()
        {
            if (Target is null)
            {
                throw new ArgumentNullException(nameof(Target));
            }
            WriteLine("Test command #1 invoked");
        }

        [FilterInvoke]
        public bool FilterInvoke() => true;
    }

    [Command(Name = "dumpheap", Help = "Test command #2")]
    public class TestCommand2 : CommandBase
    {
        [ServiceImport]
        public ITarget Target { get; set; }

        [Option(Name = "--foo", Help = "Test option.")]
        public int Foo { get; set; }

        public override void Invoke()
        {
            if (Target is null)
            {
                throw new ArgumentNullException(nameof(Target));
            }
            WriteLine("Test command #2 invoked");
        }

        [FilterInvoke]
        public bool FilterInvoke() => true;
    }

    [Command(Name = "dumpheap", Help = "Test command #3")]
    public class TestCommand3 : CommandBase
    {
        [ServiceImport]
        public ITarget Target { get; set; }

        [Option(Name = "--foo", Help = "Test option.")]
        public int Foo { get; set; }

        public override void Invoke()
        {
            if (Target is null)
            {
                throw new ArgumentNullException(nameof(Target));
            }
            WriteLine("Test command #3 invoked");
        }

        [FilterInvoke]
        public bool FilterInvoke() => false;
    }

    [Command(Name = "assemblies", Help = "Test command #4")]
    public class TestCommand4 : CommandBase
    {
        [ServiceImport]
        public ITarget Target { get; set; }

        [Option(Name = "--foo", Help = "Test option.")]
        public int Foo { get; set; }

        public override void Invoke()
        {
            if (Target is null)
            {
                throw new ArgumentNullException(nameof(Target));
            }
            WriteLine("Test command #4 invoked");
        }

        [FilterInvoke]
        public static bool FilterInvoke() => true;
    }

    [Command(Name = "ip2md", Help = "Test command #5")]
    public class TestCommand5 : CommandBase
    {
        [ServiceImport]
        public ITarget Target { get; set; }

        [Option(Name = "--bar", Help = "Test option #5.")]
        public int Foo { get; set; }

        public override void Invoke()
        {
            if (Target is null)
            {
                throw new ArgumentNullException(nameof(Target));
            }
            WriteLine("Test command #5 invoked");
        }

        [FilterInvoke]
        public static bool FilterInvoke() => false;
    }
}
