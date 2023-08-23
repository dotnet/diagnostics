// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    public struct HResult
    {
        public const int S_OK = 0;
        public const int S_FALSE = 1;
        public const int E_FAIL = unchecked((int)0x80004005);
        public const int E_INVALIDARG = unchecked((int)0x80070057);
        public const int E_NOTIMPL = unchecked((int)0x80004001);
        public const int E_NOINTERFACE = unchecked((int)0x80004002);

        public bool IsOK => Value == S_OK;

        public int Value { get; set; }

        public HResult(int hr) => Value = hr;

        public static implicit operator HResult(int hr) => new(hr);

        /// <summary>
        /// Helper to convert to int for comparisons.
        /// </summary>
        public static implicit operator int(HResult hr) => hr.Value;

        /// <summary>
        /// This makes "if (hr)" equivalent to SUCCEEDED(hr).
        /// </summary>
        public static implicit operator bool(HResult hr) => hr.Value >= 0;

        public override string ToString()
        {
            return Value switch
            {
                S_OK => "S_OK",
                S_FALSE => "S_FALSE",
                E_FAIL => "E_FAIL",
                E_INVALIDARG => "E_INVALIDARG",
                E_NOTIMPL => "E_NOTIMPL",
                E_NOINTERFACE => "E_NOINTERFACE",
                _ => $"{Value:x8}",
            };
        }
    }
}