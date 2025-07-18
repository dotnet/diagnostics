// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    [Command(Name = "testcommand", Help = "Test command #1")]
    public class TestCommand1 : CommandBase
    {
        public static bool FilterValue;
        public static bool Invoked;

        [ServiceImport]
        public ITarget Target { get; set; }

        [Argument(Name = "FileName", Help = "Test argument.")]
        public string FileName { get; set; }

        public override void Invoke()
        {
            Assert.NotNull(Target);
            Invoked = true;
        }

        [FilterInvoke]
        public bool FilterInvoke() => FilterValue;
    }

    [Command(Name = "testcommand", Help = "Test command #2")]
    public class TestCommand2 : CommandBase
    {
        public static bool FilterValue;
        public static bool Invoked;

        [ServiceImport]
        public ITarget Target { get; set; }

        [Option(Name = "--foo", Help = "Test option.")]
        public int Foo { get; set; }

        public override void Invoke()
        {
            Assert.NotNull(Target);
            Invoked = true;
        }

        [FilterInvoke(Message = "Test command #2 filter")]
        public bool FilterInvoke() => FilterValue;
    }

    [Command(Name = "testcommand", Help = "Test command #3")]
    public class TestCommand3 : CommandBase
    {
        public static bool FilterValue;
        public static bool Invoked;

        [ServiceImport]
        public ITarget Target { get; set; }

        [Option(Name = "--foo", Help = "Test option.")]
        public int Foo { get; set; }

        public override void Invoke()
        {
            Assert.NotNull(Target);
            Invoked = true;
        }

        [FilterInvoke]
        public static bool FilterInvoke() => FilterValue;
    }
}
