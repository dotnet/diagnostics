// Brain dead debuggee to test GCWhere
// Basically create a single object and ensure that it is kept
// alive and not optimized away. Verify that GC.Collect causes
// the object to pass though the GC generations until it hits
// Gen 2 where it should stay.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

class GCWhere
{
    private string _string;

    public GCWhere(string inputString)
    {
        _string = inputString;
    }
    string TempString
    {
        get
        {
            return _string;
        }
    }

    // Create an object, ensure that it is kept alive and force
    // several GC collections to happen which will cause the
    // object to move from Gen0 to Gen1 to Gen2 where it should
    // stay
    static int Main() 
    {
        GCWhere temp = new GCWhere("This is a string!!");
        Debugger.Break();   // GCWhere should temp in Gen0        
        GC.Collect();
        Debugger.Break();   // GCWhere should temp in Gen1                
        GC.Collect();
        Debugger.Break();   // GCWhere should temp in Gen2                
        GC.Collect();
        Debugger.Break();   // GCWhere should temp in Gen2                
        PrintIt(temp);
        GC.KeepAlive(temp);
        return 100;
    }

    // This is here because without calling something with the object as an argument it'll get optimized away
    static void PrintIt(GCWhere temp)
    {
        Console.WriteLine(temp.TempString);
    }
}
