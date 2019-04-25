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
	    private int maxRow;  // Running maximum of row number
	    private int maxCol;  // Running maximum of col number

		public ConsoleWriter()
		{
			displayPosition = new Dictionary<string, (int, int)>();
		}

	    public void InitializeDisplay()
	    {
	        Console.Clear();
	        origRow = Console.CursorTop;
	        origCol = Console.CursorLeft;

	        maxRow = origRow;
	        maxCol = origCol;
	    }

	    public void Update(ICounterPayload payload)
	    {
	    	string name = payload.GetName();

	    	if (displayPosition.ContainsKey(name))
	    	{
	    		(int left, int row) = displayPosition[name];
		    	Console.SetCursorPosition(left, row);
		    	Console.Write(new String(' ', 10)); // TODO: fix this

		    	Console.SetCursorPosition(left, row);//row, left);
		    	Console.Write(payload.GetValue());	
	    	}
	    	else
	    	{
	    		string displayName = payload.GetDisplay();
	    		int left = displayName.Length + 3; // displayName + " : "
	    		int row = maxRow;

	    		displayPosition[name] = (left, row);

	    		Console.SetCursorPosition(left, row);
	    		Console.Write(new String(' ', 10));
	    		Console.SetCursorPosition(left, row);
	    		Console.Write(payload.GetValue());

	    		maxRow += 1;
	    	}
	    }
	}
}