// --------------------------------------------------------------------
// 
// Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// --------------------------------------------------------------------

using System;
using System.Text;

namespace Microsoft.Diagnostic.Repl
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
