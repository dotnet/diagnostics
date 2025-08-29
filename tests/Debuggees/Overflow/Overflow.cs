// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

public class C
{
    private int m_s;

    public void RoundAndRound(int n)
    {
        if (n > 0)
            RoundAndRound(n - m_s);

        Console.WriteLine(n);
    }

    public static void Main(string[] args)
    {
        C c = new C();
        c.m_s = 0;
        c.RoundAndRound(10);
        Console.WriteLine("This should never have finished...");
    }
}
