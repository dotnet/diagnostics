using System;
using System.Threading.Tasks;
using RandomTest;

namespace SosTests
{
    /// <summary>
    /// This test creates an asynchronous task that results in an exception being thrown.
    /// </summary>
    class TaskException
    {
        static int Main()
        {
            RandomUserTask theTask = new RandomUserTask(100);
            theTask.WaitTask();

            return 0;
        }
    }
}