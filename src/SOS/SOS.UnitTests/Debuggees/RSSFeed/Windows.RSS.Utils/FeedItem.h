#pragma once

namespace Windows
{
    namespace RSS
    {
        namespace Utils
        {
            // To be bindable, a class must be defined within a namespace
            // and a bindable attribute needs to be applied.
            // A FeedItem represents a single blog post.
            [Windows::UI::Xaml::Data::Bindable]
            public ref class FeedItem sealed
            {
            public:
                FeedItem(void);

                property Platform::String^ GUID;
                property Windows::Foundation::DateTime PubDate;
                property Windows::Foundation::Uri^ Link;

                property Platform::String^ Title;

                property Platform::String^ Summary;
                property Platform::String^ Description;

                // These are all URIs to the image associated to the article.
                property Windows::Foundation::Uri^ AssetUri;
                property Windows::Foundation::Uri^ ThumbnailUri;
                property Windows::Foundation::Uri^ MainImageUri;

                //property Windows::Web::Syndication::SyndicationItem^ RootItem;

                property Windows::Foundation::Collections::IVector<Platform::String^>^ RelatedLinks
                {
                    Windows::Foundation::Collections::IVector<Platform::String^>^ get() { return m_relatedLinks; }
                }

                property Windows::Foundation::Collections::IVector<Platform::String^>^ RelatedArticles
                {
                    Windows::Foundation::Collections::IVector<Platform::String^>^ get() { return m_relatedArticles; }
                }

                static FeedItem^ ParseSyndicationItem(Windows::Web::Syndication::SyndicationItem^ feedItem);
            private:
                ~FeedItem(void);
                Platform::Collections::Vector<Platform::String^>^ m_relatedArticles;
                Platform::Collections::Vector<Platform::String^>^ m_relatedLinks;

                static Windows::Foundation::Uri^ GetUrlFromAttribute(Windows::Data::Xml::Dom::XmlNamedNodeMap^ attributes);
                static Platform::Collections::Vector<Platform::String^>^ GetRelatedGuidsFromString(Platform::String^ guidString);
            };
        }
    }
}