//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// http://code.msdn.microsoft.com/windowsapps/Windows-Store-Simple-Blog-953302e8
//*********************************************************

//feeddata.h

#pragma once
#include "pch.h"
#include "FeedItem.h"

namespace Windows
{
    namespace RSS
    {
        namespace Utils
        {
            // A FeedData object represents a feed that contains 
            // one or more FeedItems. 
            [Windows::UI::Xaml::Data::Bindable]
            public ref class FeedData sealed
            {
            public:
                FeedData(void)
                {
                    m_items = ref new Platform::Collections::Vector<FeedItem^>();
                }

                // The public members must be Windows Runtime types so that
                // the XAML controls can bind to them from a separate .winmd.
                property Platform::String^ Title;
                property Windows::Foundation::Collections::IVector<FeedItem^>^ Items
                {
                    Windows::Foundation::Collections::IVector<FeedItem^>^ get() { return m_items; }
                }

                property Platform::String^ Description;
                property Windows::Foundation::DateTime PubDate;
                property Platform::String^ Uri;

            private:
                Platform::Collections::Vector<FeedItem^>^ m_items;
                ~FeedData(void){}
            };

            // A FeedDataSource represents a collection of FeedData objects
            // and provides the methods to download the source data from which
            // FeedData and FeedItem objects are constructed. This class is 
            // instantiated at startup by this declaration in the 
            // ResourceDictionary in app.xaml: <local:FeedDataSource x:Key="feedDataSource" />
            [Windows::UI::Xaml::Data::Bindable]
            public ref class FeedDataSource sealed
            {
            public:
                FeedDataSource();
                property Windows::Foundation::Collections::IObservableVector<FeedData^>^ Feeds
                {
                    Windows::Foundation::Collections::IObservableVector<FeedData^>^ get()
                    {
                        return this->m_feeds;
                    }
                }
                Windows::Foundation::IAsyncOperation<FeedData^>^ GetFeedAsync(Platform::String^ uri);

                Windows::Foundation::IAsyncActionWithProgress<double>^ GetFeedsAsync(Windows::Foundation::Collections::IObservableVector<Platform::String^>^ feeds);

                void RemoveFeed(Platform::String^ uri);

            private:
                Platform::Collections::Vector<FeedData^>^ m_feeds;
                std::map<Platform::String^, Concurrency::task_completion_event<FeedData^>> m_feedCompletionEvents;

                FeedData^ GetFeedData(Platform::String^ feedUri, Windows::Web::Syndication::SyndicationFeed^ feed);
                void AddFeed(Platform::String^ uri);
            };
        }
    }
}