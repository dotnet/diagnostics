// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;

namespace SOS
{
    /// <summary>
    /// Simple trace/logging support.
    /// </summary>
    internal sealed class Tracer : Microsoft.SymbolStore.ITracer
    {
        readonly bool m_enabled;
        readonly bool m_enabledVerbose;
        readonly SymbolReader.WriteLine m_output;

        public Tracer(bool enabled, bool enabledVerbose, SymbolReader.WriteLine output)
        {
            m_enabled = enabled;
            m_enabledVerbose = enabledVerbose;
            m_output = output;
        }

        public void WriteLine(string message)
        {
            m_output?.Invoke(message);
        }

        public void WriteLine(string format, params object[] arguments)
        {
            WriteLine(string.Format(format, arguments));
        }

        public void Information(string message)
        {
            if (m_enabled)
            {
                WriteLine(message);
            }
        }

        public void Information(string format, params object[] arguments)
        {
            if (m_enabled)
            {
                WriteLine(format, arguments);
            }
        }

        public void Warning(string message)
        {
            if (m_enabled)
            {
                WriteLine("WARNING: " + message); 
            }
        }
            
        public void Warning(string format, params object[] arguments)
        {
            if (m_enabled)
            {
                WriteLine("WARNING: " + format, arguments);
            }
        }

        public void Error(string message)
        {
            if (m_enabled) 
            {
                WriteLine("ERROR: " + message);
            }
        }

        public void Error(string format, params object[] arguments)
        {
            if (m_enabled)
            {
                WriteLine("ERROR: " + format, arguments);
            }
        }

        public void Verbose(string message)
        {
            if (m_enabledVerbose)
            {
                WriteLine(message);
            }
        }

        public void Verbose(string format, params object[] arguments)
        {
            if (m_enabledVerbose)
            {
                WriteLine(format, arguments);
            }
        }
    }
}
