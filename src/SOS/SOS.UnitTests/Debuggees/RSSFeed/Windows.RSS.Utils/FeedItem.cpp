#include "pch.h"
#include "FeedItem.h"

using namespace Windows::RSS::Utils;

using namespace Concurrency;
using namespace Platform;
using namespace Platform::Collections;
using namespace Windows::Foundation;
using namespace Windows::Web::Syndication;

FeedItem::FeedItem(void)
{
	m_relatedArticles = ref new Vector<String^>();
	m_relatedLinks = ref new Vector<String^>();
}

FeedItem::~FeedItem(void)
{

}

FeedItem^ FeedItem::ParseSyndicationItem(Windows::Web::Syndication::SyndicationItem^ rssItem)
{
	FeedItem^ item = ref new FeedItem();
	item->GUID = rssItem->Id;
	item->Title = rssItem->Title->Text;
	item->Summary = rssItem->Summary->Text;
	item->PubDate = rssItem->PublishedDate;
	
	//item->RootItem = rssItem;

	if (rssItem->Links->Size > 0)
	{
		item->Link = ref new Uri(rssItem->Links->GetAt(0)->NodeValue);
	}
	
	auto xmlDocument = rssItem->GetXmlDocument(SyndicationFormat::Rss20);
	//auto xml = xmlDocument->GetXml();
	for (auto node : xmlDocument->DocumentElement->ChildNodes)
	{
		auto name = node->NodeName;
		auto value = node->NodeValue;
		
		if (name->Equals("asset"))
			item->AssetUri = ref new Uri(value->ToString());
		else if (name->Equals("mainimage"))
			item->MainImageUri = FeedItem::GetUrlFromAttribute(node->Attributes);
		else if (name->Equals("thumbnail"))
			item->ThumbnailUri = FeedItem::GetUrlFromAttribute(node->Attributes);
	}

	return item;
}

Uri^ FeedItem::GetUrlFromAttribute(Windows::Data::Xml::Dom::XmlNamedNodeMap^ attributes)
{
	String^ url = nullptr;
	for (auto attr : attributes)
	{
		if (attr->NodeName->Equals("url"))
		{
			url = attr->NodeValue->ToString();
			break;
		}
	}

	return url != nullptr 
		? ref new Uri(url) 
		: nullptr;
}

Vector<String^>^ FeedItem::GetRelatedGuidsFromString(Platform::String^ guidString)
{
	Vector<String^>^ vector = ref new Vector<String^>();
	const wchar_t* delimiter = L",";
	auto wcguid = guidString->Data();

	return vector;
}