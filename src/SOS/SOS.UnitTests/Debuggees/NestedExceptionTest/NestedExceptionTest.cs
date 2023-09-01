// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace NestedExceptionTest
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            try 
            {
                throw new FormatException("Bad format exception, inner");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Invalid operation exception, outer", ex);
            }
        }
    }
}
