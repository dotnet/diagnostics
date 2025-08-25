// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

internal sealed class Test
{
    private static void LikelyInlined()
    {
        Console.WriteLine("I would like to be inlined");
    }

    private static void UnlikelyInlined()
    {
        Console.Write("I");
        Console.Write(" ");
        Console.Write("w");
        Console.Write("o");
        Console.Write("u");
        Console.Write("l");
        Console.Write("d");
        Console.Write(" ");
        Console.Write("n");
        Console.Write("o");
        Console.Write("t");
        Console.Write(" ");
        Console.Write("l");
        Console.Write("i");
        Console.Write("k");
        Console.Write("e");
        Console.Write(" ");
        Console.Write("t");
        Console.Write("o");
        Console.Write(" ");
        Console.Write("b");
        Console.Write("e");
        Console.Write(" ");
        Console.Write("i");
        Console.Write("n");
        Console.Write("l");
        Console.Write("i");
        Console.Write("n");
        Console.Write("e");
        Console.Write("d");
        Console.Write("\n");
    }

    private static void ClrU()
    {
        Console.WriteLine("test dumpclass");
    }

    private static void DumpClass()
    {
        Console.WriteLine("test dumpclass");
    }

    private static void DumpIL()
    {
        Console.WriteLine("test dumpil");
    }

    private static void DumpMD()
    {
        Console.WriteLine("test dumpmd");
    }

    private static void DumpModule()
    {
        Console.WriteLine("test dumpmodule");
    }

    private static void DumpObject()
    {
        Console.WriteLine("test dumpobject");
    }

    private static void DumpStackObjects()
    {
        Console.WriteLine("test dso");
    }

    private static void Name2EE()
    {
        Console.WriteLine("test name2ee");
    }

    private static int Main()
    {
        DumpIL();
        LikelyInlined();
        UnlikelyInlined();

        return 0;
    }
}
