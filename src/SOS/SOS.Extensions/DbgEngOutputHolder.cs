using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;
using System;
using System.Runtime.InteropServices;

namespace SOS.Extensions
{
    /// <summary>
    /// A helper class to capture output of DbgEng commands that will restore the previous output callbacks
    /// when disposed.
    /// </summary>
    internal class DbgEngOutputHolder : IDebugOutputCallbacksWide, IDisposable
    {
        private readonly IDebugClient5 _client;
        private readonly IDebugOutputCallbacksWide _previous;

        public DEBUG_OUTPUT InterestMask { get; set; }

        /// <summary>
        /// Event fired when we receive output fromt he debugger.
        /// </summary>
        public Action<DEBUG_OUTPUT, string> OutputReceived;

        public DbgEngOutputHolder(IDebugClient5 client, DEBUG_OUTPUT interestMask = DEBUG_OUTPUT.NORMAL)
        {
            _client = client;
            InterestMask = interestMask;

            _client.GetOutputCallbacksWide(out _previous);
            HResult hr = _client.SetOutputCallbacksWide(this);
            if (!hr)
                throw Marshal.GetExceptionForHR(hr);
        }

        public void Dispose()
        {
            if (_previous is not null)
                _client.SetOutputCallbacksWide(_previous);
        }

        public int Output(DEBUG_OUTPUT Mask, [In, MarshalAs(UnmanagedType.LPStr)] string Text)
        {
            if ((InterestMask & Mask) != 0 && Text is not null)
                OutputReceived?.Invoke(Mask, Text);

            return 0;
        }
    }
}
