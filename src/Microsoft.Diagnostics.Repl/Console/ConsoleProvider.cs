// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Repl
{
    public sealed class ConsoleProvider : IConsole, IConsoleService
    {
        private readonly List<StringBuilder> m_history;

        private readonly CharToLineConverter m_consoleConverter;
        private readonly CharToLineConverter m_warningConverter;
        private readonly CharToLineConverter m_errorConverter;

        private string m_prompt = "> ";

        private bool m_shutdown;
        private CancellationTokenSource m_interruptExecutingCommand;

        private string m_clearLine;
        private bool m_interactiveConsole;
        private bool m_refreshingLine;
        private StringBuilder m_activeLine;

        private int m_selectedHistory;

        private bool m_modified;
        private int m_cursorPosition;
        private int m_scrollPosition;
        private bool m_insertMode;

        private int m_commandExecuting;
        private string m_lastCommandLine;

        /// <summary>
        /// Create an instance of the console provider
        /// </summary>
        /// <param name="errorColor">error color (default red)</param>
        /// <param name="warningColor">warning color (default yellow)</param>
        public ConsoleProvider(ConsoleColor errorColor = ConsoleColor.Red, ConsoleColor warningColor = ConsoleColor.Yellow)
        {
            m_history = new List<StringBuilder>();
            m_activeLine = new StringBuilder();
            m_shutdown = false;

            m_consoleConverter = new CharToLineConverter(text => {
                NewOutput(text);
            });

            m_warningConverter = new CharToLineConverter(text => {
                NewOutput(text, warningColor);
            });

            m_errorConverter = new CharToLineConverter(text => {
                NewOutput(text, errorColor);
            });

            Out = new StandardStreamWriter((text) => WriteOutput(OutputType.Normal, text));
            Error = new StandardStreamWriter((text) => WriteOutput(OutputType.Error, text));

            // Hook ctrl-C and ctrl-break
            Console.CancelKeyPress += new ConsoleCancelEventHandler(OnCtrlBreakKeyPress);
        }

        /// <summary>
        /// Start input processing and command dispatching
        /// </summary>
        /// <param name="dispatchCommand">Called to dispatch a command on ENTER</param>
        public async Task Start(Func<string, CancellationToken, Task> dispatchCommand)
        {
            m_lastCommandLine = null;
            m_interactiveConsole = !Console.IsInputRedirected;
            RefreshLine();

            // The special prompts for the test runner are built into this
            // console provider when the output has been redirected.
            if (!m_interactiveConsole) {
                WriteLine(OutputType.Normal, "<END_COMMAND_OUTPUT>");
            }

            // Start keyboard processing
            while (!m_shutdown) {
                if (m_interactiveConsole)
                {
                    ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                    await ProcessKeyInfo(keyInfo, dispatchCommand);
                }
                else
                {
                    // The input has been redirected (i.e. testing or in script)
                    string line = Console.ReadLine();
                    if (string.IsNullOrEmpty(line)) {
                        continue;
                    }
                    bool result = await Dispatch(line, dispatchCommand);
                    if (!m_shutdown)
                    {
                        if (result) {
                            WriteLine(OutputType.Normal, "<END_COMMAND_OUTPUT>");
                        }
                        else {
                            WriteLine(OutputType.Normal, "<END_COMMAND_ERROR>");
                        }
                    }
                }
            }
        }

        public bool Shutdown { get { return m_shutdown; } }

        /// <summary>
        /// Stop input processing/dispatching
        /// </summary>
        public void Stop()
        {
            ClearLine();
            m_shutdown = true;
            Console.CancelKeyPress -= new ConsoleCancelEventHandler(OnCtrlBreakKeyPress);
        }

        /// <summary>
        /// Change the command prompt
        /// </summary>
        /// <param name="prompt">new prompt</param>
        public void SetPrompt(string prompt)
        {
            m_prompt = prompt;
            RefreshLine();
        }

        /// <summary>
        /// Writes a message with a new line to console.
        /// </summary>
        public void WriteLine(string format, params object[] parameters)
        {
            WriteLine(OutputType.Normal, format, parameters);
        }

        /// <summary>
        /// Writes a message with a new line to console.
        /// </summary>
        public void WriteLine(OutputType type, string format, params object[] parameters)
        {
            WriteOutput(type, string.Format(format, parameters) + Environment.NewLine);
        }

        /// <summary>
        /// Write text on the console screen
        /// </summary>
        /// <param name="type">output type</param>
        /// <param name="message">text</param>
        /// <exception cref="OperationCanceledException">ctrl-c interrupted the command</exception>
        public void WriteOutput(OutputType type, string message)
        {
            switch (type)
            {
                case OutputType.Normal:
                    m_consoleConverter.Input(message);
                    break;

                case OutputType.Warning:
                    m_warningConverter.Input(message);
                    break;

                case OutputType.Error:
                    m_errorConverter.Input(message);
                    break;
            }
        }

        /// <summary>
        /// Clear the console screen
        /// </summary>
        public void ClearScreen()
        {
            Console.Clear();
            PrintActiveLine();
        }

        /// <summary>
        /// Write a line to the console.
        /// </summary>
        /// <param name="text">line of text</param>
        /// <param name="color">color of the text</param>
        private void NewOutput(string text, ConsoleColor? color = null)
        {
            ClearLine();

            ConsoleColor? originalColor = null;
            if (color.HasValue) {
                originalColor = Console.ForegroundColor;
                Console.ForegroundColor = color.Value;
            }
            Console.WriteLine(text);
            if (originalColor.HasValue) {
                Console.ForegroundColor = originalColor.Value;
            }

            PrintActiveLine();
        }

        /// <summary>
        /// This is the ctrl-c/ctrl-break handler
        /// </summary>
        private void OnCtrlBreakKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            if (!m_shutdown && m_interactiveConsole) {
                if (m_interruptExecutingCommand != null) {
                    m_interruptExecutingCommand.Cancel();
                }
                e.Cancel = true;
            }
        }

        private void CommandStarting()
        {
            if (m_commandExecuting == 0) {
                ClearLine();
            }
            m_commandExecuting++;
        }

        private void CommandFinished()
        {
            if (--m_commandExecuting == 0) {
                RefreshLine();
            }
        }

        private void ClearLine()
        {
            if (!m_interactiveConsole) {
                return;
            }

            if (m_commandExecuting != 0) {
                return;
            }

            if (m_clearLine == null || m_clearLine.Length != Console.WindowWidth) {
                m_clearLine = "\r" + new string(' ', Console.WindowWidth - 1);
            }

            Console.Write(m_clearLine);
            Console.CursorLeft = 0;
        }

        private void PrintActiveLine()
        {
            if (!m_interactiveConsole) {
                return;
            }

            if (m_shutdown) {
                return;
            }

            if (m_commandExecuting != 0) {
                return;
            }

            string prompt = m_prompt;

            int lineWidth = 80;
            if (Console.WindowWidth > prompt.Length) {
                lineWidth = Console.WindowWidth - prompt.Length - 1;
            }
            int scrollIncrement = lineWidth / 3;

            int activeLineLen = m_activeLine.Length;

            m_scrollPosition = Math.Min(Math.Max(m_scrollPosition, 0), activeLineLen);
            m_cursorPosition = Math.Min(Math.Max(m_cursorPosition, 0), activeLineLen);

            while (m_cursorPosition < m_scrollPosition) {
                m_scrollPosition = Math.Max(m_scrollPosition - scrollIncrement, 0);
            }

            while (m_cursorPosition - m_scrollPosition > lineWidth - 5) {
                m_scrollPosition += scrollIncrement;
            }

            int lineRest = activeLineLen - m_scrollPosition;
            int max = Math.Min(lineRest, lineWidth);
            string text = m_activeLine.ToString(m_scrollPosition, max);

            Console.Write("{0}{1}", prompt, text);
            Console.CursorLeft = prompt.Length + (m_cursorPosition - m_scrollPosition);
        }

        private void RefreshLine()
        {
            // Check for recursions.
            if (m_refreshingLine) {
                return;
            }
            m_refreshingLine = true;
            ClearLine();
            PrintActiveLine();
            m_refreshingLine = false;
        }

        private async Task ProcessKeyInfo(ConsoleKeyInfo keyInfo, Func<string, CancellationToken, Task> dispatchCommand)
        {
            int activeLineLen = m_activeLine.Length;

            switch (keyInfo.Key) {
                case ConsoleKey.Backspace: // The BACKSPACE key. 
                    if (m_cursorPosition > 0) {
                        EnsureNewEntry();
                        m_activeLine.Remove(m_cursorPosition - 1, 1);
                        m_cursorPosition--;
                        RefreshLine();
                    }
                    break;

                case ConsoleKey.Insert: // The INS (INSERT) key. 
                    m_insertMode = !m_insertMode;
                    RefreshLine();
                    break;

                case ConsoleKey.Delete: // The DEL (DELETE) key. 
                    if (m_cursorPosition < activeLineLen) {
                        EnsureNewEntry();
                        m_activeLine.Remove(m_cursorPosition, 1);
                        RefreshLine();
                    }
                    break;

                case ConsoleKey.Enter: // The ENTER key. 
                    string newCommand = m_activeLine.ToString();

                    if (m_modified) {
                        m_history.Add(m_activeLine);
                    }
                    m_selectedHistory = m_history.Count;

                    await Dispatch(newCommand, dispatchCommand);

                    SwitchToHistoryEntry();
                    break;

                case ConsoleKey.Escape: // The ESC (ESCAPE) key. 
                    EnsureNewEntry();
                    m_activeLine.Clear();
                    m_cursorPosition = 0;
                    RefreshLine();
                    break;

                case ConsoleKey.End: // The END key. 
                    m_cursorPosition = activeLineLen;
                    RefreshLine();
                    break;

                case ConsoleKey.Home: // The HOME key. 
                    m_cursorPosition = 0;
                    RefreshLine();
                    break;

                case ConsoleKey.LeftArrow: // The LEFT ARROW key. 
                    if (keyInfo.Modifiers == ConsoleModifiers.Control) {
                        while (m_cursorPosition > 0 && char.IsWhiteSpace(m_activeLine[m_cursorPosition - 1])) {
                            m_cursorPosition--;
                        }

                        while (m_cursorPosition > 0 && !char.IsWhiteSpace(m_activeLine[m_cursorPosition - 1])) {
                            m_cursorPosition--;
                        }
                    }
                    else {
                        m_cursorPosition--;
                    }

                    RefreshLine();
                    break;

                case ConsoleKey.UpArrow: // The UP ARROW key. 
                    if (m_selectedHistory > 0) {
                        m_selectedHistory--;
                    }
                    SwitchToHistoryEntry();
                    break;

                case ConsoleKey.RightArrow: // The RIGHT ARROW key. 
                    if (keyInfo.Modifiers == ConsoleModifiers.Control) {
                        while (m_cursorPosition < activeLineLen && !char.IsWhiteSpace(m_activeLine[m_cursorPosition])) {
                            m_cursorPosition++;
                        }

                        while (m_cursorPosition < activeLineLen && char.IsWhiteSpace(m_activeLine[m_cursorPosition])) {
                            m_cursorPosition++;
                        }
                    }
                    else {
                        m_cursorPosition++;
                    }

                    RefreshLine();
                    break;

                case ConsoleKey.DownArrow: // The DOWN ARROW key. 
                    if (m_selectedHistory < m_history.Count) {
                        m_selectedHistory++;
                    }
                    SwitchToHistoryEntry();

                    RefreshLine();
                    break;

                default:
                    if (keyInfo.KeyChar != 0) {
                        if ((keyInfo.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0) {
                            AppendNewText(new string(keyInfo.KeyChar, 1));
                        }
                    }
                    break;
            }
        }

        private async Task<bool> Dispatch(string newCommand, Func<string, CancellationToken, Task> dispatchCommand)
        {
            bool result = true;
            CommandStarting();
            m_interruptExecutingCommand = new CancellationTokenSource();
            try
            {
                newCommand = newCommand.Trim();
                if (string.IsNullOrEmpty(newCommand) && m_lastCommandLine != null) {
                    newCommand = m_lastCommandLine;
                }
                try
                {
                    WriteLine(OutputType.Normal, "{0}{1}", m_prompt, newCommand);
                    await dispatchCommand(newCommand, m_interruptExecutingCommand.Token);
                    m_lastCommandLine = newCommand;
                }
                catch (OperationCanceledException)
                {
                    // ctrl-c interrupted the command
                    m_lastCommandLine = null;
                }
                catch (Exception ex) when (!(ex is NullReferenceException || ex is ArgumentNullException || ex is ArgumentException))
                {
                    WriteLine(OutputType.Error, "ERROR: {0}", ex.Message);
                    m_lastCommandLine = null;
                    result = false;
                }
            }
            finally
            {
                m_interruptExecutingCommand = null;
                CommandFinished();
            }
            return result;
        }

        private void AppendNewText(string text)
        {
            EnsureNewEntry();

            foreach (char c in text) {
                // Filter unwanted characters.
                switch (c) {
                    case '\t':
                    case '\r':
                    case '\n':
                        continue;
                }

                if (m_insertMode && m_cursorPosition < m_activeLine.Length) {
                    m_activeLine[m_cursorPosition] = c;
                }
                else {
                    m_activeLine.Insert(m_cursorPosition, c);
                }
                m_modified = true;
                m_cursorPosition++;
            }

            RefreshLine();
        }

        private void SwitchToHistoryEntry()
        {
            if (m_selectedHistory < m_history.Count) {
                m_activeLine = m_history[m_selectedHistory];
            }
            else {
                m_activeLine = new StringBuilder();
            }

            m_cursorPosition = m_activeLine.Length;
            m_modified = false;

            RefreshLine();
        }

        private void EnsureNewEntry()
        {
            if (!m_modified) {
                m_activeLine = new StringBuilder(m_activeLine.ToString());
                m_modified = true;
            }
        }

        #region IConsole

        public IStandardStreamWriter Out { get; }

        bool IStandardOut.IsOutputRedirected { get { return false; } }

        public IStandardStreamWriter Error { get; }

        bool IStandardError.IsErrorRedirected { get { return false; } }

        bool IStandardIn.IsInputRedirected { get { return false; } }

        class StandardStreamWriter : IStandardStreamWriter
        {
            readonly Action<string> _write;

            public StandardStreamWriter(Action<string> write) => _write = write;

            void IStandardStreamWriter.Write(string value) => _write(value);
        }

        #endregion

        #region IConsoleService

        void IConsoleService.Write(string text) => WriteOutput(OutputType.Normal, text);

        void IConsoleService.WriteError(string text) => WriteOutput(OutputType.Error, text);

        void IConsoleService.Exit() => Stop();

        #endregion
    }
}
