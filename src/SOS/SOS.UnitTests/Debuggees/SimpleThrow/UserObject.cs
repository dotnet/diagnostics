using System;

public interface IUserObject
{
    void UseObject(string argument);
}

public class UserObject : IUserObject
{
    public void UseObject(string argument)
    {
        Console.WriteLine("argument passed: " + argument);
        int i = 1;
        if (i == 1)
        {
            throw new InvalidOperationException("Throwing an invalid operation....");
        }
    }
}
