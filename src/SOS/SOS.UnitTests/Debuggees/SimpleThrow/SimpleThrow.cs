// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

internal class Simple
{
    private static int Main()
    {
        Console.WriteLine("This is some simple exception.");
        IUserObject testObject = new UserObject();
        testObject.UseObject("A string!");

        return 0;
    }
}