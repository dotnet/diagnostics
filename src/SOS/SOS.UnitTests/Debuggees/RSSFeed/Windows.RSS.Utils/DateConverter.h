//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// http://code.msdn.microsoft.com/windowsapps/Windows-Store-Simple-Blog-953302e8
//*********************************************************

//DateConverter.h

#pragma once
#include <string> //for wcscmp

namespace Windows
{
    namespace RSS
    {
        namespace Utils
        {
            public ref class DateConverter sealed : public Windows::UI::Xaml::Data::IValueConverter
            {
            public:
                virtual Platform::Object^ Convert(Platform::Object^ value,
                    Windows::UI::Xaml::Interop::TypeName targetType,
                    Platform::Object^ parameter,
                    Platform::String^ language)
                {
                    if (value == nullptr)
                    {
                        throw ref new Platform::InvalidArgumentException();
                    }
                    auto dt = safe_cast<Windows::Foundation::DateTime>(value);
                    auto param = safe_cast<Platform::String^>(parameter);
                    Platform::String^ result;
                    if (param == nullptr)
                    {
                        auto dtf =
                            Windows::Globalization::DateTimeFormatting::DateTimeFormatter::ShortDate::get();
                        result = dtf->Format(dt);
                    }
                    else if (wcscmp(param->Data(), L"month") == 0)
                    {
                        auto month =
                            ref new Windows::Globalization::DateTimeFormatting::DateTimeFormatter("{month.abbreviated(3)}");
                        result = month->Format(dt);
                    }
                    else if (wcscmp(param->Data(), L"day") == 0)
                    {
                        auto month =
                            ref new Windows::Globalization::DateTimeFormatting::DateTimeFormatter("{day.integer(2)}");
                        result = month->Format(dt);
                    }
                    else if (wcscmp(param->Data(), L"year") == 0)
                    {
                        auto month =
                            ref new Windows::Globalization::DateTimeFormatting::DateTimeFormatter("{year.full}");
                        result = month->Format(dt);
                    }
                    else
                    {
                        // We don't handle other format types currently.
                        throw ref new Platform::InvalidArgumentException();
                    }

                    return result;
                }

                virtual Platform::Object^ ConvertBack(Platform::Object^ value,
                    Windows::UI::Xaml::Interop::TypeName targetType,
                    Platform::Object^ parameter,
                    Platform::String^ language)
                {
                    // Not needed in Windows. Left as an exercise.
                    throw ref new Platform::NotImplementedException();
                }
            };
        }
    }
}