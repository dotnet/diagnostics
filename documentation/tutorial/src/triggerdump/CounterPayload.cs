// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
            m_Value = payloadFields["Mean"].ToString();
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
        public IncrementingCounterPayload(IDictionary<string, object> payloadFields)
        {
            m_Name = payloadFields["Name"].ToString();
            m_Value = payloadFields["Increment"].ToString();
            m_DisplayName = payloadFields["DisplayName"].ToString();
            m_DisplayRateTimeScale = TimeSpan.Parse(payloadFields["DisplayRateTimeScale"].ToString()).ToString("%s' sec'");
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
