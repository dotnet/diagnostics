using System;

namespace RSSFeedTest
{
    public static class AsyncWinRTTest
    {
        public static void Main()
        {
            // Bad URL
            string badURL =  "http://feeds.aljazeeraportal.net/AJE/live/1/200779101832373555?removehtml=1";
            string goodURL = "http://feeds.aljazeeraportal.net/aje/live/1/200779101832373555?removehtml=1";
            TestRSSFeed feed = new TestRSSFeed(goodURL);
            Console.WriteLine("Throwing from asyncAction with Progress");
            var theTask = feed.ThrowFromAsyncActionWithProgress();
            theTask.Wait();
            Console.WriteLine("The Program is exiting....");
            return;
        }
    }
}