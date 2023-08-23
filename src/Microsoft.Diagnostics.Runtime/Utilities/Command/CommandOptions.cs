// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// CommandOptions is a helper class for the Command class.  It stores options
    /// that affect the behavior of the execution of ETWCommands and is passes as a
    /// parameter to the constructor of a Command.
    /// It is useful for these options be on a separate class (rather than
    /// on Command itself), because it is reasonably common to want to have a set
    /// of options passed to several commands, which is not easily possible otherwise.
    /// </summary>
    internal sealed class CommandOptions
    {
        internal bool noThrow;
        internal bool useShellExecute;
        internal bool noWindow;
        internal bool noWait;
        internal bool elevate;
        internal int timeoutMSec;
        internal string? input;
        internal string? outputFile;
        internal TextWriter? outputStream;
        internal string? currentDirectory;
        internal Dictionary<string, string>? environmentVariables;

        /// <summary>
        /// Can be assigned to the Timeout Property to indicate infinite timeout.
        /// </summary>
        public const int Infinite = System.Threading.Timeout.Infinite;

        /// <summary>
        /// CommanOptions holds a set of options that can be passed to the constructor
        /// to the Command Class as well as Command.Run*.
        /// </summary>
        public CommandOptions()
        {
            timeoutMSec = 600000;
        }

        /// <summary>
        /// Return a copy an existing set of command options.
        /// </summary>
        /// <returns>The copy of the command options.</returns>
        public CommandOptions Clone()
        {
            return (CommandOptions)MemberwiseClone();
        }

        /// <summary>
        /// Normally commands will throw if the subprocess returns a non-zero
        /// exit code.  NoThrow suppresses this.
        /// </summary>
        public bool NoThrow
        {
            get => noThrow;
            set => noThrow = value;
        }

        /// <summary>
        /// Updates the NoThrow property and returns the updated commandOptions.
        /// <returns>Updated command options</returns>
        /// </summary>
        public CommandOptions AddNoThrow()
        {
            noThrow = true;
            return this;
        }

        /// <summary>
        /// ShortHand for UseShellExecute and NoWait.
        /// </summary>
        public bool Start
        {
            get => useShellExecute;
            set
            {
                useShellExecute = value;
                noWait = value;
            }
        }

        /// <summary>
        /// Updates the Start property and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddStart()
        {
            Start = true;
            return this;
        }

        /// <summary>
        /// Normally commands are launched with CreateProcess.  However it is
        /// also possible use the Shell Start API.  This causes Command to look
        /// up the executable differently.
        /// </summary>
        public bool UseShellExecute
        {
            get => useShellExecute;
            set => useShellExecute = value;
        }

        /// <summary>
        /// Updates the Start property and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddUseShellExecute()
        {
            useShellExecute = true;
            return this;
        }

        /// <summary>
        /// Indicates that you want to hide any new window created.
        /// </summary>
        public bool NoWindow
        {
            get => noWindow;
            set => noWindow = value;
        }

        /// <summary>
        /// Updates the NoWindow property and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddNoWindow()
        {
            noWindow = true;
            return this;
        }

        /// <summary>
        /// Indicates that you want don't want to wait for the command to complete.
        /// </summary>
        public bool NoWait
        {
            get => noWait;
            set => noWait = value;
        }

        /// <summary>
        /// Updates the NoWait property and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddNoWait()
        {
            noWait = true;
            return this;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the command must run at elevated Windows privileges (causes a new command window).
        /// </summary>
        public bool Elevate
        {
            get => elevate;
            set => elevate = value;
        }

        /// <summary>
        /// Updates the Elevate property and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddElevate()
        {
            elevate = true;
            return this;
        }

        /// <summary>
        /// By default commands have a 10 minute timeout (600,000 msec), If this
        /// is inappropriate, the Timeout property can change this.  Like all
        /// timeouts in .NET, it is in units of milliseconds, and you can use
        /// CommandOptions.Infinite to indicate no timeout.
        /// </summary>
        public int Timeout
        {
            get => timeoutMSec;
            set => timeoutMSec = value;
        }

        /// <summary>
        /// Updates the Timeout property and returns the updated commandOptions.
        /// CommandOptions.Infinite can be used for infinite.
        /// </summary>
        public CommandOptions AddTimeout(int milliseconds)
        {
            timeoutMSec = milliseconds;
            return this;
        }

        /// <summary>
        /// Indicates the string will be sent to Console.In for the subprocess.
        /// </summary>
        public string? Input
        {
            get => input;
            set => input = value;
        }

        /// <summary>
        /// Updates the Input property and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddInput(string input)
        {
            this.input = input;
            return this;
        }

        /// <summary>
        /// Indicates the current directory the subProcess will have.
        /// </summary>
        public string? CurrentDirectory
        {
            get => currentDirectory;
            set => currentDirectory = value;
        }

        /// <summary>
        /// Updates the CurrentDirectory property and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddCurrentDirectory(string directoryPath)
        {
            currentDirectory = directoryPath;
            return this;
        }

        // TODO add a capability to return a enumerator of output lines. (and/or maybe a delegate callback)

        /// <summary>
        /// Indicates the standard output and error of the command should be redirected
        /// to a archiveFile rather than being stored in Memory in the 'Output' property of the
        /// command.
        /// </summary>
        public string? OutputFile
        {
            get => outputFile;
            set
            {
                if (outputStream != null)
                    throw new Exception("OutputFile and OutputStream can not both be set");

                outputFile = value;
            }
        }

        /// <summary>
        /// Updates the OutputFile property and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddOutputFile(string outputFile)
        {
            OutputFile = outputFile;
            return this;
        }

        /// <summary>
        /// Indicates the standard output and error of the command should be redirected
        /// to a a TextWriter rather than being stored in Memory in the 'Output' property
        /// of the command.
        /// </summary>
        public TextWriter? OutputStream
        {
            get => outputStream;
            set
            {
                if (outputFile != null)
                    throw new Exception("OutputFile and OutputStream can not both be set");

                outputStream = value;
            }
        }

        /// <summary>
        /// Updates the OutputStream property and returns the updated commandOptions.
        /// </summary>
        public CommandOptions AddOutputStream(TextWriter outputStream)
        {
            OutputStream = outputStream;
            return this;
        }

        /// <summary>
        /// Gets the Environment variables that will be set in the subprocess that
        /// differ from current process's environment variables.  Any time a string
        /// of the form %VAR% is found in a value of a environment variable it is
        /// replaced with the value of the environment variable at the time the
        /// command is launched.  This is useful for example to update the PATH
        /// environment variable eg. "%PATH%;someNewPath".
        /// </summary>
        public Dictionary<string, string> EnvironmentVariables
        {
            get
            {
                environmentVariables ??= new Dictionary<string, string>();
                return environmentVariables;
            }
        }

        /// <summary>
        /// Adds the environment variable with the give value to the set of
        /// environment variables to be passed to the sub-process and returns the
        /// updated commandOptions.   Any time a string
        /// of the form %VAR% is found in a value of a environment variable it is
        /// replaced with the value of the environment variable at the time the
        /// command is launched.  This is useful for example to update the PATH
        /// environment variable eg. "%PATH%;someNewPath".
        /// </summary>
        public CommandOptions AddEnvironmentVariable(string variable, string value)
        {
            EnvironmentVariables[variable] = value;
            return this;
        }

        // We are friends with the Command class

        // TODO implement
        // internal bool showCommand;          // Show the command before running it.
    }
}
