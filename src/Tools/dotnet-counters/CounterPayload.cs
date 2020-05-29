﻿// Licensed to the .NET Foundation under one or more agreements.
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
        double GetValue();
        string GetDisplay();
        string GetCounterType();
    }


    class CounterPayload : ICounterPayload
    {
        public string m_Name;
        public double m_Value;
        public string m_DisplayName;
        public string m_DisplayUnits;

        public CounterPayload(IDictionary<string, object> payloadFields)
        {
            m_Name = payloadFields["Name"].ToString();
            m_Value = (double)payloadFields["Mean"];
            m_DisplayName = payloadFields["DisplayName"].ToString();
            m_DisplayUnits = payloadFields["DisplayUnits"].ToString();

            // In case these properties are not provided, set them to appropriate values.
            m_DisplayName = m_DisplayName.Length == 0 ? m_Name : m_DisplayName;
        }

        public string GetName()
        {
            return m_Name;
        }

        public double GetValue()
        {
            return m_Value;
        }

        public string GetDisplay()
        {
            if (m_DisplayUnits.Length > 0)
            {
                return $"{m_DisplayName} ({m_DisplayUnits})";
            }
            return $"{m_DisplayName}";
        }

        public string GetCounterType()
        {
            return "Metric";
        }
    }

    class IncrementingCounterPayload : ICounterPayload
    {
        public string m_Name;
        public double m_Value;
        public string m_DisplayName;
        public string m_Interval;
        public string m_DisplayUnits;

        public IncrementingCounterPayload(IDictionary<string, object> payloadFields, int interval)
        {
            m_Name = payloadFields["Name"].ToString();
            m_Value = (double)payloadFields["Increment"];
            m_DisplayName = payloadFields["DisplayName"].ToString();
            m_DisplayUnits = payloadFields["DisplayUnits"].ToString();
            m_Interval = interval.ToString() + " sec";

            // In case these properties are not provided, set them to appropriate values.
            m_DisplayName = m_DisplayName.Length == 0 ? m_Name : m_DisplayName;
        }

        public string GetName()
        {
            return m_Name;
        }

        public double GetValue()
        {
            return m_Value;
        }

        public string GetDisplay()
        {
            if (m_DisplayUnits.Length > 0)
                return $"{m_DisplayName} / {m_Interval} ({m_DisplayUnits})";

            return $"{m_DisplayName} / {m_Interval}";
        }

        public string GetCounterType()
        {
            return "Rate";
        }
    }
}
