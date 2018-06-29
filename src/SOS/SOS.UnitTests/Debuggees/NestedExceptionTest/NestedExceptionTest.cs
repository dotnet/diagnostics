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
