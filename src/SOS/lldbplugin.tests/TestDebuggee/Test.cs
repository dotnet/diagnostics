// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

internal static class Test
{
    public static void LikelyInlined()
    {
        Console.WriteLine("I would like to be inlined");
    }

    public static void UnlikelyInlined()
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

    public static void ClrU()
    {
        Console.WriteLine("test dumpclass");
    }

    public static void DumpClass()
    {
        Console.WriteLine("test dumpclass");
    }

    public static void DumpIL()
    {
        Console.WriteLine("test dumpil");
    }

    public static void DumpMD()
    {
        Console.WriteLine("test dumpmd");
    }

    public static void DumpModule()
    {
        Console.WriteLine("test dumpmodule");
    }

    public static void DumpObject()
    {
        Console.WriteLine("test dumpobject");
    }

    public static void DumpStackObjects()
    {
        Console.WriteLine("test dso");
    }

    public static void Name2EE()
    {
        Console.WriteLine("test name2ee");
    }


    public static int Main()
    {
        DumpIL();
        LikelyInlined();
        UnlikelyInlined();

        return 0;
    }
}
