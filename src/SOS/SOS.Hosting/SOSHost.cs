// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SOS
{
    /// <summary>
    /// Helper code to hosting SOS under ClrMD
    /// </summary>
    public sealed class SOSHost
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int SOSCommandDelegate(IntPtr ILLDBServices, [In, MarshalAs(UnmanagedType.LPStr)] string args);

        readonly LLDBServicesWrapper _wrapper;
        IntPtr _sosLibrary = IntPtr.Zero;

        /// <summary>
        /// Enable the assembly resolver to get the right SOS.NETCore version (the one
        /// in the same directory as SOS.Hosting).
        /// </summary>
        static SOSHost()
        {
            AssemblyResolver.Enable();
        }

        /// <summary>
        /// The native SOS binaries path. Default is OS/architecture (RID) named directory in the same directory as this assembly.
        /// </summary>
        public string SOSPath { get; set; }

        /// <summary>
        /// Create an instance of the hosting class
        /// </summary>
        public SOSHost(IDataReader dataReader, ISOSHostContext context)
        {
            string os = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                os = "win";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                os = "linux";
            }
            if (os == null) {
                throw new PlatformNotSupportedException($"{RuntimeInformation.OSDescription} not supported");
            }
            string architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            string rid = os + "-" + architecture;
            SOSPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), rid);

            _wrapper = new LLDBServicesWrapper(this, dataReader, context);
        }

        public void ExecuteCommand(string commandLine)
        {
            string command = "Help";
            string arguments = null;

            if (commandLine != null)
            {
                int firstSpace = commandLine.IndexOf(' ');
                command = firstSpace == -1 ? commandLine : commandLine.Substring(0, firstSpace);
                arguments = firstSpace == -1 ? null : commandLine.Substring(firstSpace);
            }
            ExecuteCommand(command, arguments);
        }

        public void ExecuteCommand(string command, string arguments)
        {
            if (_sosLibrary == IntPtr.Zero)
            {
                string sosPath = Path.Combine(SOSPath, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "sos.dll" : "libsos.so");
                _sosLibrary = DataTarget.PlatformFunctions.LoadLibrary(sosPath);
                if (_sosLibrary == IntPtr.Zero)
                {
                    throw new FileNotFoundException($"SOS module {sosPath} not found");
                }
            }
            IntPtr commandAddress = DataTarget.PlatformFunctions.GetProcAddress(_sosLibrary, command);
            if (commandAddress == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException($"Can not find SOS command: {command}");
            }
            var commandFunc = (SOSCommandDelegate)Marshal.GetDelegateForFunctionPointer(commandAddress, typeof(SOSCommandDelegate));

            int result = commandFunc(_wrapper.ILLDBServices, arguments ?? "");
            if (result != 0)
            {
                throw new InvalidOperationException($"SOS command FAILED 0x{result:X8}");
            }
        }
    }
}
