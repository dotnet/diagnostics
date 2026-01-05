// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;

public class AsyncMainTest
{
    public static async Task<int> Main(string[] args)
    {
        DivideData data = new()
        {
            Numerator = 16,
            Denominator = 0,
        };

        Console.WriteLine($"{data.Numerator}/{data.Denominator} = {await DivideAsync(data)}");
        return 0;
    }

    static async Task<int> DivideAsync(DivideData data)
    {
        await Task.Delay(10);
        return data.Numerator / data.Denominator;
    }
}

public class DivideData
{
    public int Numerator { get; set; }
    public int Denominator { get; set; }
}
