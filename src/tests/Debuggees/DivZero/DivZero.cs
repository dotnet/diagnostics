// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;using System.Security;
#if WINCORESYS
[assembly: AllowPartiallyTrustedCallers]
#endif
public class C{
	public static void DivideByZero(ref int v, ref int w)
	{
		int x;
		int y = 1;

		y--;
		x = 2 / y;
	}

	public static void F3(ref int m, ref int n)
	{
		int a = 1;
		int b = 2;
		int c;

		DivideByZero(ref a, ref b); // pass locals by ref to prevent enregistering

		c = a + b;
		Console.WriteLine("a + b = {0}", c);
	}

	public static void F2()
	{
		int p = 3;
		int q = 4;
		int r;

		F3(ref p, ref q); // pass locals by ref to prevent enregistering

		r = p * q;
		Console.WriteLine("p * q = {0}", r);
	}
	
	public static void F1()
	{
		try
		{
			F2();
		}
		catch (DivideByZeroException)
		{
			Console.WriteLine("F2 catch");
		}
	}

	public static void Main(string[] args)
	{
		F1();
		F2();
	}


    // This method should be called to pass SoS.DivZero test.
    // We need to figure out how to call it from Main.
	public static void R()
	{
		int m = 5;
		int n = 6;
		bool f = true;

		// We want to test two types of Exception("Application Error Message")s.
		// Divide by zero is a hardware fault, resulting
		// no stack space movement.
		// The two cases is a ">" versus ">=" test.
		DivideByZero(ref m, ref n);
		
		if (f)	// this is always true, just to silence a compiler warning
			throw new ArgumentException("Application Error Message");
		
		Console.WriteLine("m - n = {0}", m-n);
	}
}
