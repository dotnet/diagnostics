//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// http://code.msdn.microsoft.com/windowsapps/Windows-Store-Simple-Blog-953302e8
//*********************************************************

#include "pch.h"
#include "FeedData.h"

using namespace std;
using namespace Concurrency;
using namespace Platform;
using namespace Platform::Collections;
using namespace Windows::Foundation;
using namespace Windows::Web::Syndication;
using namespace Windows::RSS::Utils;

FeedDataSource::FeedDataSource()
{
	m_feeds = ref new Vector<FeedData^>();
}

// We use this method to get the proper FeedData object when resuming
// from shutdown. We need to wait for this data to be populated before
// we attempt to restore page state. Note the use of task_completion_event
// which doesn't block the UI thread.
IAsyncOperation<FeedData^>^ FeedDataSource::GetFeedAsync(String^ uri)
{
	return create_async([uri, this]()
	{
		//auto feedDataSource = safe_cast<FeedDataSource^>(
		//    App::Current->Resources->Lookup("feedDataSource"));
		auto iterator = this->m_feedCompletionEvents.find(uri);
		if (iterator == this->m_feedCompletionEvents.end())
			this->AddFeed(uri);

		// Does not block the UI thread.
		auto f = this->m_feedCompletionEvents[uri];

		// In the callers we continue from this task after the event is 
		// set in InitDataSource and we know we have a FeedData^.
		task<FeedData^> t = create_task(f);
		return t;
	});
}

void FeedDataSource::RemoveFeed(String^ uri)
{
	int num = m_feedCompletionEvents.erase(uri);
	String^ debug = L"Removed: " + num.ToString();
	OutputDebugString(debug->Data());

	int index = -1;
	for (auto feed : m_feeds)
	{
		if (feed->Uri->Equals(uri))
			break;

		index++;
	}
	if (index == -1)
		OutputDebugString(L"COULD NOT Find the feed to remove!");
	else
		m_feeds->RemoveAt(index);
}

void FeedDataSource::AddFeed(String^ uri) {
	task_completion_event<FeedData^> taskCompletionEvent;
	m_feedCompletionEvents.insert(make_pair(uri, taskCompletionEvent));

	SyndicationClient^ client = ref new SyndicationClient();
	auto feedUri = ref new Uri(uri);

	create_task(client->RetrieveFeedAsync(feedUri))
		.then([this, uri](SyndicationFeed^ feed) -> FeedData^
	{
		return GetFeedData(uri, feed);
	}, concurrency::task_continuation_context::use_arbitrary())
		.then([this](FeedData^ fd)
	{
		m_feeds->Append(fd);
		m_feedCompletionEvents[fd->Uri].set(fd);

		// Write to VS output window in debug mode only. Requires <windows.h>.
		OutputDebugString(fd->Title->Data());
		OutputDebugString(L"\r\n");
	})
		.then([](task<void> t)
	{
		// The last continuation serves as an error handler.
		try
		{
			t.get();
		}
		// SyndicationClient throws Platform::InvalidArgumentException 
		// if a URL contains illegal characters.
		// We catch this exception for demonstration purposes only.
		// In the current design of this app, an illegal
		// character can only be introduced by a coding error
		// and should not be caught. If we modify the app to allow
		// the user to manually add a new url, then we need to catch
		// the exception.
		catch (Platform::InvalidArgumentException^ e)
		{
			// For example purposes we just output error to console.
			// In a real world app that allowed the user to enter
			// a url manually, you could prompt them to try again.
			OutputDebugString(e->Message->Data());
		}
	}); //end task chain
}

FeedData^ FeedDataSource::GetFeedData(String^ feedUri, SyndicationFeed^ feed)
{
	FeedData^ feedData = ref new FeedData();

	// Knowing this makes it easier to map completion_events 
	// when we resume from termination.
	feedData->Uri = feedUri;

	// Get the title of the feed (not the individual posts).
	feedData->Title = feed->Title->Text;

	if (feed->Subtitle->Text != nullptr)
	{
		feedData->Description = feed->Subtitle->Text;
	}
	// Use the date of the latest post as the last updated date.
	feedData->PubDate = feed->Items->GetAt(0)->PublishedDate;
	// Construct a FeedItem object for each post in the feed
	// using a range-based for loop. Preferable to a 
	// C-style for loop, or std::for_each.
	for (auto item : feed->Items)
	{
		auto feedItem = FeedItem::ParseSyndicationItem(item);
		feedData->Items->Append(feedItem);
	};

	return feedData;
}

IAsyncActionWithProgress<double>^ FeedDataSource::GetFeedsAsync(Windows::Foundation::Collections::IObservableVector<String^>^ feeds)
{
	int first = 0;
	int last = 20;
	return create_async([feeds](progress_reporter<double> reporter)
	{
		throw ref new InvalidArgumentException("Some exception thrown from GetFeedsAsync.");
	});
}