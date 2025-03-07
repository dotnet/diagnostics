// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using static System.CommandLine.Rendering.Interop;

namespace System.CommandLine.Rendering
{
    /// <summary>
    /// This file is a copy of https://github.com/dotnet/command-line-api/blob/060374e56c1b2e741b6525ca8417006efb54fbd7/src/System.CommandLine.Rendering/Interop.Windows.cs
    /// which is no longer supported.
    /// </summary>
    public sealed class VirtualTerminalMode : IDisposable
    {
        private readonly IntPtr _stdOutHandle;
        private readonly IntPtr _stdInHandle;
        private readonly uint _originalOutputMode;
        private readonly uint _originalInputMode;

        private VirtualTerminalMode(bool isEnabled)
        {
            IsEnabled = isEnabled;
            GC.SuppressFinalize(this); // ctor used only on Unix, where there is nothing to cleanup
        }

        private VirtualTerminalMode(
            IntPtr stdOutHandle,
            uint originalOutputMode,
            IntPtr stdInHandle,
            uint originalInputMode)
        {
            _stdOutHandle = stdOutHandle;
            _originalOutputMode = originalOutputMode;
            _stdInHandle = stdInHandle;
            _originalInputMode = originalInputMode;
        }

        public bool IsEnabled { get; }

        public static VirtualTerminalMode TryEnable()
        {
            if (OperatingSystem.IsWindows())
            {
                IntPtr stdOutHandle = GetStdHandle(STD_OUTPUT_HANDLE);
                IntPtr stdInHandle = GetStdHandle(STD_INPUT_HANDLE);

                if (!GetConsoleMode(stdOutHandle, out uint originalOutputMode))
                {
                    return null;
                }

                if (!GetConsoleMode(stdInHandle, out uint originalInputMode))
                {
                    return null;
                }

                uint requestedOutputMode = originalOutputMode |
                                          ENABLE_VIRTUAL_TERMINAL_PROCESSING |
                                          DISABLE_NEWLINE_AUTO_RETURN;

                if (!SetConsoleMode(stdOutHandle, requestedOutputMode))
                {
                    return null;
                }

                return new VirtualTerminalMode(stdOutHandle,
                                               originalOutputMode,
                                               stdInHandle,
                                               originalInputMode);
            }
            else
            {
                string terminalName = Environment.GetEnvironmentVariable("TERM");

                bool isXterm = !string.IsNullOrEmpty(terminalName)
                              && terminalName.StartsWith("xterm", StringComparison.OrdinalIgnoreCase);

                // TODO: Is this a reasonable default?
                return new VirtualTerminalMode(isXterm);
            }
        }

        private void RestoreConsoleMode()
        {
            if (IsEnabled)
            {
                if (_stdOutHandle != IntPtr.Zero)
                {
                    SetConsoleMode(_stdOutHandle, _originalOutputMode);
                }
            }
        }

        public void Dispose()
        {
            RestoreConsoleMode();
            GC.SuppressFinalize(this);
        }

        ~VirtualTerminalMode()
        {
            RestoreConsoleMode();
        }
    }
}
