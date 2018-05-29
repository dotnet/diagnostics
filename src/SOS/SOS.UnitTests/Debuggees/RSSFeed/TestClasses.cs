using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.RSS.Utils;
using Windows.Foundation.Collections;

namespace RSSFeedTest
{
	public class TestRSSFeed
    {
        public readonly string FeedUri;

        private FeedItem[] m_feedItems;
        private readonly FeedDataSource m_feedDataSource;

        public TestRSSFeed(string feedURI)
        {
            FeedUri = feedURI;
            m_feedDataSource = new FeedDataSource();
        }

        public async void Initialize()
        {
            var iasyncOp = m_feedDataSource.GetFeedAsync(FeedUri);

            await iasyncOp.AsTask<FeedData>()
                .ContinueWith(t =>
                {
                    FeedData rssFeed = t.Result;

                    for (int i = 0; i < rssFeed.Items.Count; i++)
                    {
                        var item = rssFeed.Items[i];
                        string newSummary = ParseFeedSummary(item.Summary);
                        item.Summary = newSummary;
                    }

                    return rssFeed;
                })
                .ContinueWith((Task<FeedData> t, object _) =>
                {
                    var rssFeed = t.Result;
                    m_feedItems = new FeedItem[rssFeed.Items.Count];
                    for (int i = 0; i < rssFeed.Items.Count; i++)
                    {
                        var item = rssFeed.Items[i];
                        m_feedItems[i] = item;
                    }
                }, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public async Task ThrowFromAsyncActionWithProgress()
        {
            TestObservableVector<string> vector = new TestObservableVector<string>();
            vector.Add(FeedUri);
        	Console.WriteLine("Calling into winrt code...");
        	await m_feedDataSource.GetFeedsAsync(vector);
        	Console.WriteLine("Should not finish!");
        }

        /// <summary>
        /// This removes all of those HTML escaped characters and the image URL 
        /// at the beginning of the RSS feed description.
        /// I hacked this into C# because it is easier than writing the parsing 
        /// code into the winRT component. ;(
        /// </summary>
        private static string ParseFeedSummary(string description)
        {
            string fixedDescription = description.Replace("&amp;", "&");
            fixedDescription = fixedDescription.Replace("&lt;", "<");
            fixedDescription = fixedDescription.Replace("&gt;", ">");

            return fixedDescription;
        }
    }

    public class TestObservableVector<T> : List<T>, IObservableVector<T>
    {
        public event VectorChangedEventHandler<T> VectorChanged;

        public TestObservableVector()
        {
            VectorChanged = delegate(IObservableVector<T> sender, IVectorChangedEventArgs e) { };
        }
    }
}