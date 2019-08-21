// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public interface ICounterPayload
    {
        string GetName();
        string GetValue();
        string GetDisplay();
    }

    class CounterPayload : ICounterPayload
    {
        public string m_Name;
        public string m_Value;
        public string m_DisplayName;

        public CounterPayload(IDictionary<string, object> payloadFields)
        {
            m_Name = payloadFields["Name"].ToString();
            m_Value = Int32.Parse(payloadFields["Mean"].ToString()).ToString("N0");
            m_DisplayName = payloadFields["DisplayName"].ToString();
        }

        public string GetName()
        {
            return m_Name;
        }

        public string GetValue()
        {
            return m_Value;
        }

        public string GetDisplay()
        {
            return m_DisplayName;
        }
    }

    class IncrementingCounterPayload : ICounterPayload
    {
        public string m_Name;
        public string m_Value;
        public string m_DisplayName;
        public string m_DisplayRateTimeScale;

        public IncrementingCounterPayload(IDictionary<string, object> payloadFields, int interval)
        {
            m_Name = payloadFields["Name"].ToString();
            m_Value = Int32.Parse(payloadFields["Increment"].ToString()).ToString("N0");
            m_DisplayName = payloadFields["DisplayName"].ToString();
            m_DisplayRateTimeScale = payloadFields["DisplayRateTimeScale"].ToString();

            // In case these properties are not provided, set them to appropriate values.
            m_DisplayName = m_DisplayName.Length == 0 ? m_Name : m_DisplayName;
            m_DisplayRateTimeScale = m_DisplayRateTimeScale.Length == 0 ? $"{interval} sec" : TimeSpan.Parse(m_DisplayRateTimeScale).ToString("%s' sec'");
        }

        public string GetName()
        {
            return m_Name;
        }

        public string GetValue()
        {
            return m_Value;
        }

        public string GetDisplay()
        {
            return $"{m_DisplayName} / {m_DisplayRateTimeScale}";
        }
    }
}
