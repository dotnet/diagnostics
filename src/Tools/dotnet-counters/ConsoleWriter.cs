// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;


namespace Microsoft.Diagnostics.Tools.Counters
{
	public class ConsoleWriter
	{
	    private Dictionary<string, (int, int)> displayPosition; // Display position (x-y coordiates) of each counter values.
	    private int origRow;
	    private int origCol;

		public ConsoleWriter()
		{
			displayPosition = new Dictionary<string, (int, int)>();
		}

	    public void InitializeDisplay()
	    {
	        Console.Clear();
	        origRow = Console.CursorTop;
	        origCol = Console.CursorLeft;

	        int rowCnt = 0;

	        foreach (CounterProvider provider in KnownData.GetAllProviders())
	        {
	        	Console.WriteLine(provider.Name);
	        	rowCnt += 1;

	        	foreach (CounterProfile counter in provider.Counters.Values)
	        	{
	        		Console.WriteLine($"    {counter.Name} : 0");
	        		displayPosition[counter.Name] = (counter.Name.Length+4+3, rowCnt);
	        		rowCnt += 1;
	        	}
	        }
	    }

	    public void Update(string counterName, string val)
	    {
	    	(int left, int row) = displayPosition[counterName];
	    	Console.SetCursorPosition(left, row);
	    	Console.Write(new String(' ', 10)); // TODO: fix this

	    	Console.SetCursorPosition(left, row);//row, left);
	    	Console.Write(val);
	    }
	}
}