﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Text;

namespace Microsoft.Diagnostics.Repl
{
    public sealed class CharToLineConverter
    {
        readonly Action<string> m_callback;
        readonly StringBuilder m_text = new StringBuilder();

        public CharToLineConverter(Action<string> callback)
        {
            m_callback = callback;
        }

        public void Input(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++) {
                char c = (char)buffer[offset + i];
                if (c == '\r') {
                    continue;
                }
                if (c == '\n') {
                    Flush();
                }
                else if (c == '\t' || (c >= (char)0x20 && c <= (char)127)) {
                    m_text.Append(c);
                }
            }
        }

        public void Input(string text)
        {
            foreach (char c in text) {
                if (c == '\r') {
                    continue;
                }
                if (c == '\n') {
                    Flush();
                }
                else if (c == '\t' || (c >= (char)0x20 && c <= (char)127)) {
                    m_text.Append(c);
                }
            }
        }

        public void Flush()
        {
            m_callback(m_text.ToString());
            m_text.Clear();
        }
    }
}
