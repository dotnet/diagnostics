using System;

class Simple
{
    static int Main()
    {
        Console.WriteLine("This is some simple exception.");
        IUserObject testObject = new UserObject();
        testObject.UseObject("A string!");

        return 0;
    }
}