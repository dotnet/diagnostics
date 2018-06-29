using System;

public class C
{
    int m_s;

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
