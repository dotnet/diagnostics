// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System;
using System.Diagnostics;
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
        private delegate int SOSCommandDelegate(
            IntPtr ILLDBServices,
            [In, MarshalAs(UnmanagedType.LPStr)] string args);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SOSInitializeDelegate(
            [In, MarshalAs(UnmanagedType.Struct)] ref SOSNetCoreCallbacks callbacks,
            int callbacksSize,
            [In, MarshalAs(UnmanagedType.LPStr)] string tempDirectory, 
            [In, MarshalAs(UnmanagedType.LPStr)] string dacFilePath, 
            [In, MarshalAs(UnmanagedType.LPStr)] string dbiFilePath,
            bool symbolStoreEnabled);

        private const string SOSInitialize = "SOSInitializeByHost";

        #region SOS.NETCore function delegates

        private delegate bool InitializeSymbolStoreDelegate(
            bool logging,
            bool msdl,
            bool symweb,
            string symbolServerPath,
            string symbolCachePath,
            string windowsSymbolPath);

        private delegate void DisplaySymbolStoreDelegate();

        private delegate void DisableSymbolStoreDelegate();

        private delegate void LoadNativeSymbolsDelegate(
            SymbolReader.SymbolFileCallback callback,
            IntPtr parameter,
            string tempDirectory,
            string moduleFilePath,
            ulong address,
            int size,
            SymbolReader.ReadMemoryDelegate readMemory);

        private delegate IntPtr LoadSymbolsForModuleDelegate(
            string assemblyPath,
            bool isFileLayout,
            ulong loadedPeAddress,
            int loadedPeSize,
            ulong inMemoryPdbAddress,
            int inMemoryPdbSize,
            SymbolReader.ReadMemoryDelegate readMemory);

        private delegate void DisposeDelegate(IntPtr symbolReaderHandle);

        private delegate bool ResolveSequencePointDelegate(
            IntPtr symbolReaderHandle,
            string filePath,
            int lineNumber,
            out int methodToken,
            out int ilOffset);

        private delegate bool GetLineByILOffsetDelegate(
            IntPtr symbolReaderHandle,
            int methodToken,
            long ilOffset,
            out int lineNumber,
            out IntPtr fileName);

        private delegate bool GetLocalVariableNameDelegate(
            IntPtr symbolReaderHandle,
            int methodToken,
            int localIndex,
            out IntPtr localVarName);

        private delegate int GetMetadataLocatorDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string imagePath,
            uint imageTimestamp,
            uint imageSize,
            [MarshalAs(UnmanagedType.LPArray, SizeConst = 16)] byte[] mvid,
            uint mdRva,
            uint flags,
            uint bufferSize,
            IntPtr buffer,
            IntPtr dataSize);

        #endregion

        /// <summary>
        /// Pass to SOSInitializeByHost
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct SOSNetCoreCallbacks
        {
            public InitializeSymbolStoreDelegate InitializeSymbolStoreDelegate;
            public DisplaySymbolStoreDelegate DisplaySymbolStoreDelegate;
            public DisableSymbolStoreDelegate DisableSymbolStoreDelegate;
            public LoadNativeSymbolsDelegate LoadNativeSymbolsDelegate;
            public LoadSymbolsForModuleDelegate LoadSymbolsForModuleDelegate;
            public DisposeDelegate DisposeDelegate;
            public ResolveSequencePointDelegate ResolveSequencePointDelegate;
            public GetLineByILOffsetDelegate GetLineByILOffsetDelegate;
            public GetLocalVariableNameDelegate GetLocalVariableNameDelegate;
            public GetMetadataLocatorDelegate GetMetadataLocatorDelegate;
        }

        static SOSNetCoreCallbacks s_callbacks = new SOSNetCoreCallbacks {
            InitializeSymbolStoreDelegate = SymbolReader.InitializeSymbolStore,
            DisplaySymbolStoreDelegate = SymbolReader.DisplaySymbolStore,
            DisableSymbolStoreDelegate = SymbolReader.DisableSymbolStore,
            LoadNativeSymbolsDelegate = SymbolReader.LoadNativeSymbols,
            LoadSymbolsForModuleDelegate = SymbolReader.LoadSymbolsForModule,
            DisposeDelegate  = SymbolReader.Dispose,
            ResolveSequencePointDelegate = SymbolReader.ResolveSequencePoint,
            GetLineByILOffsetDelegate = SymbolReader.GetLineByILOffset,
            GetLocalVariableNameDelegate = SymbolReader.GetLocalVariableName,
            GetMetadataLocatorDelegate = MetadataHelper.GetMetadataLocator
        };

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
            string rid = InstallHelper.GetRid();
            SOSPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), rid);
            _wrapper = new LLDBServicesWrapper(this, dataReader, context);
        }

        /// <summary>
        /// Loads and initializes the SOS module.
        /// </summary>
        /// <param name="tempDirectory">Temporary directory created to download DAC module</param>
        /// <param name="dacFilePath">The path to DAC that CLRMD loaded or downloaded or null</param>
        /// <param name="dbiFilePath">The path to DBI (for future use) or null</param>
        public void InitializeSOSHost(string tempDirectory, string dacFilePath, string dbiFilePath)
        {
            if (_sosLibrary == IntPtr.Zero)
            {
                string sosPath = Path.Combine(SOSPath, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "sos.dll" : "libsos.so");
                try
                {
                    _sosLibrary = DataTarget.PlatformFunctions.LoadLibrary(sosPath);
                }
                catch (DllNotFoundException ex)
                {
                    // This is a workaround for the Microsoft SDK docker images. Can fail when LoadLibrary uses libdl.so to load the SOS module.
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        throw new DllNotFoundException("Problem loading SOS module. Try installing libc6-dev (apt-get install libc6-dev) to work around this problem.", ex);
                    }
                    else
                    {
                        throw;
                    }
                }
                if (_sosLibrary == IntPtr.Zero)
                {
                    throw new FileNotFoundException($"SOS module {sosPath} not found");
                }
                IntPtr initializeAddress = DataTarget.PlatformFunctions.GetProcAddress(_sosLibrary, SOSInitialize);
                if (initializeAddress == IntPtr.Zero)
                {
                    throw new EntryPointNotFoundException($"Can not find SOS module initialization function: {SOSInitialize}");
                }
                var initializeFunc = (SOSInitializeDelegate)Marshal.GetDelegateForFunctionPointer(initializeAddress, typeof(SOSInitializeDelegate));

                // SOS depends on that the temp directory ends with "/".
                if (!string.IsNullOrEmpty(tempDirectory) && tempDirectory[tempDirectory.Length - 1] != Path.DirectorySeparatorChar)
                {
                    tempDirectory = tempDirectory + Path.DirectorySeparatorChar;
                }

                int result = initializeFunc(
                    ref s_callbacks,
                    Marshal.SizeOf<SOSNetCoreCallbacks>(), 
                    tempDirectory,
                    dacFilePath,
                    dbiFilePath,
                    SymbolReader.IsSymbolStoreEnabled());

                if (result != 0)
                {
                    throw new InvalidOperationException($"SOS initialization FAILED 0x{result:X8}");
                }
            }
        }

        /// <summary>
        /// Execute a SOS command.
        /// </summary>
        /// <param name="commandLine">command name and arguments</param>
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

        /// <summary>
        /// Execute a SOS command.
        /// </summary>
        /// <param name="command">just the command name</param>
        /// <param name="arguments">the command arguments and options</param>
        public void ExecuteCommand(string command, string arguments)
        {
            Debug.Assert(_sosLibrary != null);

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
