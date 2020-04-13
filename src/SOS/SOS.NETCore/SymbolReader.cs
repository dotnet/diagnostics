// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using Microsoft.SymbolStore.SymbolStores;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SOS
{
    public class SymbolReader
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DebugInfo
        {
            public int lineNumber;
            public int ilOffset;
            public string fileName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct LocalVarInfo
        {
            public int startOffset;
            public int endOffset;
            public string name;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MethodDebugInfo
        {
            public IntPtr points;
            public int size;
            public IntPtr locals;
            public int localsSize;
        }

        /// <summary>
        /// Matches the IRuntime::RuntimeConfiguration in runtime.h
        /// </summary>
        public enum RuntimeConfiguration
        {
            WindowsDesktop  = 0,
            WindowsCore     = 1,
            UnixCore        = 2,
            OSXCore         = 3
        }

        private sealed class OpenedReader : IDisposable
        {
            public readonly MetadataReaderProvider Provider;
            public readonly MetadataReader Reader;

            public OpenedReader(MetadataReaderProvider provider, MetadataReader reader)
            {
                Debug.Assert(provider != null);
                Debug.Assert(reader != null);

                Provider = provider;
                Reader = reader;
            }

            public void Dispose() => Provider.Dispose();
        }

        /// <summary>
        /// Stream implementation to read debugger target memory for in-memory PDBs
        /// </summary>
        private class TargetStream : Stream
        {
            readonly ulong _address;
            readonly ReadMemoryDelegate _readMemory;

            public override long Position { get; set; }
            public override long Length { get; }
            public override bool CanSeek { get { return true; } }
            public override bool CanRead { get { return true; } }
            public override bool CanWrite { get { return false; } }

            public TargetStream(ulong address, int size, ReadMemoryDelegate readMemory)
                : base()
            {
                Debug.Assert(address != 0);
                Debug.Assert(size != 0);
                _address = address;
                _readMemory = readMemory;
                Length = size;
                Position = 0;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Position + count > Length) {
                    throw new ArgumentOutOfRangeException();
                }
                unsafe {
                    fixed (byte* p = &buffer[offset]) {
                        int read = _readMemory(_address + (ulong)Position, p, count);
                        Position += read;
                        return read;
                    }
                }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                switch (origin) {
                    case SeekOrigin.Begin:
                        Position = offset;
                        break;
                    case SeekOrigin.End:
                        Position = Length + offset;
                        break;
                    case SeekOrigin.Current:
                        Position += offset;
                        break;
                }
                return Position;
            }

            public override void Flush()
            {
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Symbol server URLs
        /// </summary>
        const string MsdlSymbolServer = "http://msdl.microsoft.com/download/symbols/";
        const string SymwebSymbolServer = "http://symweb.corp.microsoft.com/";

        /// <summary>
        /// Read memory callback
        /// </summary>
        /// <returns>number of bytes read or 0 for error</returns>
        public unsafe delegate int ReadMemoryDelegate(ulong address, byte* buffer, int count);

        /// <summary>
        /// Writeline delegate for symbol store logging
        /// </summary>
        /// <param name="message"></param>
        public delegate void WriteLine([MarshalAs(UnmanagedType.LPStr)] string message);

        /// <summary>
        /// The LoadNativeSymbols callback
        /// </summary>
        /// <param name="moduleFileName">module file name</param>
        /// <param name="symbolFileName">symbol file name and path</param>
        public delegate void SymbolFileCallback(IntPtr parameter, [MarshalAs(UnmanagedType.LPStr)] string moduleFileName, [MarshalAs(UnmanagedType.LPStr)] string symbolFileName);

        /// <summary>
        /// Temporary directory for dac/symbols
        /// </summary>
        public static string TempDirectory { get; private set; }

        static readonly ITracer s_tracer = new Tracer();
        static SymbolStore s_symbolStore = null;

        /// <summary>
        /// Initializes symbol loading. Adds the symbol server and/or the cache path (if not null) to the list of
        /// symbol servers. This API can be called more than once to add more servers to search.
        /// </summary>
        /// <param name="logging">if true, enable logging diagnostics to console</param>
        /// <param name="msdl">if true, use the public Microsoft server</param>
        /// <param name="symweb">if true, use symweb internal server and protocol (file.ptr)</param>
        /// <param name="tempDirectory">temp directory unique to this instance of SOS</param>
        /// <param name="symbolServerPath">symbol server url (optional)</param>
        /// <param name="timeoutInMinutes">symbol server timeout in minutes (optional)</param>
        /// <param name="symbolCachePath">symbol cache directory path (optional)</param>
        /// <param name="symbolDirectoryPath">symbol directory path to search (optional)</param>
        /// <param name="windowsSymbolPath">windows symbol path (optional)</param>
        /// <returns>if false, failure</returns>
        public static bool InitializeSymbolStore(
            bool logging,
            bool msdl,
            bool symweb,
            string tempDirectory,
            string symbolServerPath,
            int timeoutInMinutes,
            string symbolCachePath,
            string symbolDirectoryPath,
            string windowsSymbolPath)
        {
            if (logging) {
                // Uses the standard console to do the logging instead of sending it to the hosting debugger console
                // because windbg/cdb can only output on the client thread without dead locking. Microsoft.SymbolStore
                // can log on any thread.
                Trace.Listeners.Add(new TextWriterTraceListener(Console.OpenStandardOutput()));
            }
            if (TempDirectory == null) {
                TempDirectory = tempDirectory;
            }
            SymbolStore store = s_symbolStore;

            // Build the symbol stores
            if (windowsSymbolPath != null) {
                // Parse the Windows symbol path syntax (srv*, cache*, etc)
                if (!ParseSymbolPath(ref store, windowsSymbolPath)) {
                    return false;
                }
            }
            else {
                // Add the default symbol cache if no cache specified and adding server
                if (symbolCachePath == null)
                {
                    if (msdl || symweb || symbolServerPath != null) {
                        symbolCachePath = GetDefaultSymbolCache();
                    }
                }
                // Build the symbol stores using the other parameters
                if (!GetServerSymbolStore(ref store, msdl, symweb, symbolServerPath, timeoutInMinutes, symbolCachePath, symbolDirectoryPath)) {
                    return false;
                }
            }

            s_symbolStore = store;
            return true;
        }

        /// <summary>
        /// Displays the symbol server and cache configuration
        /// </summary>
        public static void DisplaySymbolStore(WriteLine writeLine)
        {
            SymbolStore symbolStore = s_symbolStore;
            while (symbolStore != null)
            {
                writeLine(symbolStore.ToString());
                symbolStore = symbolStore.BackingStore;
            }
        }

        /// <summary>
        /// This function disables any symbol downloading support.
        /// </summary>
        public static void DisableSymbolStore()
        {
            s_symbolStore = null;
        }

        /// <summary>
        /// Load native symbols and modules (i.e. DAC, DBI).
        /// </summary>
        /// <param name="callback">called back for each symbol file loaded</param>
        /// <param name="parameter">callback parameter</param>
        /// <param name="config">Target configuration: Windows, Linux or OSX</param>
        /// <param name="moduleFilePath">module path</param>
        /// <param name="address">module base address</param>
        /// <param name="size">module size</param>
        /// <param name="readMemory">read memory callback delegate</param>
        public static void LoadNativeSymbols(
            SymbolFileCallback callback,
            IntPtr parameter,
            RuntimeConfiguration config,
            string moduleFilePath,
            ulong address,
            int size,
            ReadMemoryDelegate readMemory)
        {
            if (IsSymbolStoreEnabled())
            {
                Stream stream = new TargetStream(address, size, readMemory);
                KeyGenerator generator = null;

                switch (config)
                {
                    case RuntimeConfiguration.UnixCore:
                        var elfFile = new ELFFile(new StreamAddressSpace(stream), 0, true);
                        generator = new ELFFileKeyGenerator(s_tracer, elfFile, moduleFilePath);
                        break;
                    case RuntimeConfiguration.OSXCore:
                        var machOFile = new MachOFile(new StreamAddressSpace(stream), 0, true);
                        generator = new MachOFileKeyGenerator(s_tracer, machOFile, moduleFilePath);
                        break;
                    case RuntimeConfiguration.WindowsCore:
                    case RuntimeConfiguration.WindowsDesktop:
                        var peFile = new PEFile(new StreamAddressSpace(stream), true);
                        generator = new PEFileKeyGenerator(s_tracer, peFile, moduleFilePath);
                        break;
                    default:
                        s_tracer.Error("Unsupported platform {0}", config);
                        return;
                }

                try
                {
                    IEnumerable<SymbolStoreKey> keys = generator.GetKeys(KeyTypeFlags.SymbolKey | KeyTypeFlags.DacDbiKeys);
                    foreach (SymbolStoreKey key in keys)
                    {
                        string moduleFileName = Path.GetFileName(key.FullPathName);
                        s_tracer.Verbose("{0} {1}", key.FullPathName, key.Index);

                        string downloadFilePath = GetSymbolFile(key);
                        if (downloadFilePath != null)
                        {
                            s_tracer.Information("{0}: {1}", moduleFileName, downloadFilePath);
                            callback(parameter, moduleFileName, downloadFilePath);
                        }
                    }
                }
                catch (Exception ex) when (ex is BadInputFormatException || ex is InvalidVirtualAddressException || ex is TaskCanceledException)
                {
                    s_tracer.Error("{0}/{1:X16}: {2}", moduleFilePath, address, ex.Message);
                }
            }
        }

        /// <summary>
        /// Load native modules (i.e. DAC, DBI) from the runtime build id.
        /// </summary>
        /// <param name="callback">called back for each symbol file loaded</param>
        /// <param name="parameter">callback parameter</param>
        /// <param name="config">Target configuration: Windows, Linux or OSX</param>
        /// <param name="moduleFilePath">module path</param>
        /// <param name="specialKeys">if true, returns the DBI/DAC keys, otherwise the identity key</param>
        /// <param name="moduleIndexSize">build id size</param>
        /// <param name="moduleIndex">pointer to build id</param>
        public static void LoadNativeSymbolsFromIndex(
            SymbolFileCallback callback,
            IntPtr parameter,
            RuntimeConfiguration config,
            string moduleFilePath,
            bool specialKeys,
            int moduleIndexSize,
            IntPtr moduleIndex)
        {
            try
            {
                KeyTypeFlags flags = specialKeys ? KeyTypeFlags.DacDbiKeys : KeyTypeFlags.IdentityKey;
                byte[] id = new byte[moduleIndexSize];
                Marshal.Copy(moduleIndex, id, 0, moduleIndexSize);

                IEnumerable<SymbolStoreKey> keys = null;
                switch (config)
                {
                    case RuntimeConfiguration.UnixCore:
                        keys = ELFFileKeyGenerator.GetKeys(flags, moduleFilePath, id, symbolFile: false, symbolFileName: null);
                        break;

                    case RuntimeConfiguration.OSXCore:
                        keys = MachOFileKeyGenerator.GetKeys(flags, moduleFilePath, id, symbolFile: false, symbolFileName: null);
                        break;

                    case RuntimeConfiguration.WindowsCore:
                    case RuntimeConfiguration.WindowsDesktop:
                        uint timeStamp = BitConverter.ToUInt32(id, 0);
                        uint fileSize = BitConverter.ToUInt32(id, 4);
                        SymbolStoreKey key = PEFileKeyGenerator.GetKey(moduleFilePath, timeStamp, fileSize);
                        keys = new SymbolStoreKey[] { key };
                        break;

                    default:
                        s_tracer.Error("Unsupported platform {0}", config);
                        return;
                }
                foreach (SymbolStoreKey key in keys)
                {
                    string moduleFileName = Path.GetFileName(key.FullPathName);
                    s_tracer.Verbose("{0} {1}", key.FullPathName, key.Index);

                    string downloadFilePath = GetSymbolFile(key);
                    if (downloadFilePath != null)
                    {
                        s_tracer.Information("{0}: {1}", moduleFileName, downloadFilePath);
                        callback(parameter, moduleFileName, downloadFilePath);
                    }
                }
            }
            catch (Exception ex) when (ex is BadInputFormatException || ex is InvalidVirtualAddressException || ex is TaskCanceledException)
            {
                s_tracer.Error("{0} - {1}", ex.Message, moduleFilePath);
            }
        }

        /// <summary>
        /// Load symbol or module files from the keys
        /// </summary>
        /// <param name="callback">called back for each symbol file loaded</param>
        /// <param name="parameter">callback parameter</param>
        /// <param name="keys">list of keys to download</param>
        private static void LoadSymbolFiles(SymbolFileCallback callback, IntPtr parameter, IEnumerable<SymbolStoreKey> keys)
        {
        }

        /// <summary>
        /// Download a symbol from the symbol stores/server.
        /// </summary>
        /// <param name="key">index of the file to download</param>
        /// <returns>Path to the downloaded file either in the cache or in the temp directory</returns>
        public static string GetSymbolFile(SymbolStoreKey key)
        {
            string downloadFilePath = null;

            if (IsSymbolStoreEnabled())
            {
                using (SymbolStoreFile file = GetSymbolStoreFile(key))
                {
                    if (file != null)
                    {
                        try
                        {
                            downloadFilePath = file.FileName;

                            // Make sure the stream is at the beginning of the module
                            file.Stream.Position = 0;

                            // If the downloaded doesn't already exists on disk in the cache, then write it to a temporary location.
                            if (!File.Exists(downloadFilePath))
                            {
                                if (TempDirectory == null)
                                {
                                    int processId = Process.GetCurrentProcess().Id;
                                    TempDirectory = Path.Combine(Path.GetTempPath(), "sos" + processId.ToString());
                                    Directory.CreateDirectory(TempDirectory);
                                }
                                downloadFilePath = Path.Combine(TempDirectory, Path.GetFileName(key.FullPathName));

                                using (Stream destinationStream = File.OpenWrite(downloadFilePath)) {
                                    file.Stream.CopyTo(destinationStream);
                                }
                                s_tracer.WriteLine("Downloaded symbol file {0}", key.FullPathName);
                            }
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
                        {
                            s_tracer.Error("{0}: {1}", file.FileName, ex.Message);
                            downloadFilePath = null;
                        }
                    }
                }
            }

            return downloadFilePath;
        }

        /// <summary>
        /// Checks availability of debugging information for given assembly.
        /// </summary>
        /// <param name="assemblyPath">
        /// File path of the assembly or null if the module is in-memory or dynamic (generated by Reflection.Emit)
        /// </param>
        /// <param name="isFileLayout">type of in-memory PE layout, if true, file based layout otherwise, loaded layout</param>
        /// <param name="loadedPeAddress">
        /// Loaded PE image address or zero if the module is dynamic (generated by Reflection.Emit). 
        /// Dynamic modules have their PDBs (if any) generated to an in-memory stream 
        /// (pointed to by <paramref name="inMemoryPdbAddress"/> and <paramref name="inMemoryPdbSize"/>).
        /// </param>
        /// <param name="loadedPeSize">loaded PE image size</param>
        /// <param name="inMemoryPdbAddress">in memory PDB address or zero</param>
        /// <param name="inMemoryPdbSize">in memory PDB size</param>
        /// <returns>Symbol reader handle or zero if error</returns>
        public static IntPtr LoadSymbolsForModule(string assemblyPath, bool isFileLayout, ulong loadedPeAddress, int loadedPeSize, 
            ulong inMemoryPdbAddress, int inMemoryPdbSize, ReadMemoryDelegate readMemory)
        {
            try
            {
                TargetStream peStream = null;
                if (loadedPeAddress != 0)
                {
                    peStream = new TargetStream(loadedPeAddress, loadedPeSize, readMemory);
                }
                TargetStream pdbStream = null;
                if (inMemoryPdbAddress != 0)
                {
                    pdbStream = new TargetStream(inMemoryPdbAddress, inMemoryPdbSize, readMemory);
                }
                OpenedReader openedReader = GetReader(assemblyPath, isFileLayout, peStream, pdbStream);
                if (openedReader != null)
                {
                    GCHandle gch = GCHandle.Alloc(openedReader);
                    return GCHandle.ToIntPtr(gch);
                }
            }
            catch
            {
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Cleanup and dispose of symbol reader handle
        /// </summary>
        /// <param name="symbolReaderHandle">symbol reader handle returned by LoadSymbolsForModule</param>
        public static void Dispose(IntPtr symbolReaderHandle)
        {
            Debug.Assert(symbolReaderHandle != IntPtr.Zero);
            try
            {
                GCHandle gch = GCHandle.FromIntPtr(symbolReaderHandle);
                ((OpenedReader)gch.Target).Dispose();
                gch.Free();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Returns method token and IL offset for given source line number.
        /// </summary>
        /// <param name="symbolReaderHandle">symbol reader handle returned by LoadSymbolsForModule</param>
        /// <param name="filePath">source file name and path</param>
        /// <param name="lineNumber">source line number</param>
        /// <param name="methodToken">method token return</param>
        /// <param name="ilOffset">IL offset return</param>
        /// <returns> true if information is available</returns>
        public static bool ResolveSequencePoint(IntPtr symbolReaderHandle, string filePath, int lineNumber, out int methodToken, out int ilOffset)
        {
            Debug.Assert(symbolReaderHandle != IntPtr.Zero);
            methodToken = 0;
            ilOffset = 0;

            GCHandle gch = GCHandle.FromIntPtr(symbolReaderHandle);
            MetadataReader reader = ((OpenedReader)gch.Target).Reader;

            try
            {
                string fileName = GetFileName(filePath);
                foreach (MethodDebugInformationHandle methodDebugInformationHandle in reader.MethodDebugInformation)
                {
                    MethodDebugInformation methodDebugInfo = reader.GetMethodDebugInformation(methodDebugInformationHandle);
                    SequencePointCollection sequencePoints = methodDebugInfo.GetSequencePoints();
                    foreach (SequencePoint point in sequencePoints)
                    {
                        string sourceName = reader.GetString(reader.GetDocument(point.Document).Name);
                        if (point.StartLine == lineNumber && GetFileName(sourceName) == fileName)
                        {
                            methodToken = MetadataTokens.GetToken(methodDebugInformationHandle.ToDefinitionHandle());
                            ilOffset = point.Offset;
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        /// <summary>
        /// Returns source line number and source file name for given IL offset and method token.
        /// </summary>
        /// <param name="symbolReaderHandle">symbol reader handle returned by LoadSymbolsForModule</param>
        /// <param name="methodToken">method token</param>
        /// <param name="ilOffset">IL offset</param>
        /// <param name="lineNumber">source line number return</param>
        /// <param name="fileName">source file name return</param>
        /// <returns> true if information is available</returns>
        public static bool GetLineByILOffset(IntPtr symbolReaderHandle, int methodToken, long ilOffset, out int lineNumber, out IntPtr fileName)
        {
            lineNumber = 0;
            fileName = IntPtr.Zero;

            string sourceFileName = null;

            if (!GetSourceLineByILOffset(symbolReaderHandle, methodToken, ilOffset, out lineNumber, out sourceFileName))
            {
                return false;
            }
            fileName = Marshal.StringToBSTR(sourceFileName);
            sourceFileName = null;
            return true;
        }

        /// <summary>
        /// Helper method to return source line number and source file name for given IL offset and method token.
        /// </summary>
        /// <param name="symbolReaderHandle">symbol reader handle returned by LoadSymbolsForModule</param>
        /// <param name="methodToken">method token</param>
        /// <param name="ilOffset">IL offset</param>
        /// <param name="lineNumber">source line number return</param>
        /// <param name="fileName">source file name return</param>
        /// <returns> true if information is available</returns>
        private static bool GetSourceLineByILOffset(IntPtr symbolReaderHandle, int methodToken, long ilOffset, out int lineNumber, out string fileName)
        {
            Debug.Assert(symbolReaderHandle != IntPtr.Zero);
            lineNumber = 0;
            fileName = null;

            GCHandle gch = GCHandle.FromIntPtr(symbolReaderHandle);
            MetadataReader reader = ((OpenedReader)gch.Target).Reader;

            try
            {
                Handle handle = MetadataTokens.Handle(methodToken);
                if (handle.Kind != HandleKind.MethodDefinition)
                    return false;

                MethodDebugInformationHandle methodDebugHandle = ((MethodDefinitionHandle)handle).ToDebugInformationHandle();
                if (methodDebugHandle.IsNil)
                    return false;

                MethodDebugInformation methodDebugInfo = reader.GetMethodDebugInformation(methodDebugHandle);
                SequencePointCollection sequencePoints = methodDebugInfo.GetSequencePoints();

                SequencePoint? nearestPoint = null;
                foreach (SequencePoint point in sequencePoints)
                {
                    if (point.Offset > ilOffset)
                        break;

                    if (point.StartLine != 0 && !point.IsHidden)
                        nearestPoint = point;
                }

                if (nearestPoint.HasValue)
                {
                    lineNumber = nearestPoint.Value.StartLine;
                    fileName = reader.GetString(reader.GetDocument(nearestPoint.Value.Document).Name);
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        /// <summary>
        /// Returns local variable name for given local index and IL offset.
        /// </summary>
        /// <param name="symbolReaderHandle">symbol reader handle returned by LoadSymbolsForModule</param>
        /// <param name="methodToken">method token</param>
        /// <param name="localIndex">local variable index</param>
        /// <param name="localVarName">local variable name return</param>
        /// <returns>true if name has been found</returns>
        public static bool GetLocalVariableName(IntPtr symbolReaderHandle, int methodToken, int localIndex, out IntPtr localVarName)
        {
            localVarName = IntPtr.Zero;

            string localVar = null;
            if (!GetLocalVariableByIndex(symbolReaderHandle, methodToken, localIndex, out localVar))
                return false;

            localVarName = Marshal.StringToBSTR(localVar);
            localVar = null;
            return true;
        }

        /// <summary>
        /// Helper method to return local variable name for given local index and IL offset.
        /// </summary>
        /// <param name="symbolReaderHandle">symbol reader handle returned by LoadSymbolsForModule</param>
        /// <param name="methodToken">method token</param>
        /// <param name="localIndex">local variable index</param>
        /// <param name="localVarName">local variable name return</param>
        /// <returns>true if name has been found</returns>
        private static bool GetLocalVariableByIndex(IntPtr symbolReaderHandle, int methodToken, int localIndex, out string localVarName)
        {
            Debug.Assert(symbolReaderHandle != IntPtr.Zero);
            localVarName = null;

            GCHandle gch = GCHandle.FromIntPtr(symbolReaderHandle);
            MetadataReader reader = ((OpenedReader)gch.Target).Reader;

            try
            {
                Handle handle = MetadataTokens.Handle(methodToken);
                if (handle.Kind != HandleKind.MethodDefinition)
                    return false;

                MethodDebugInformationHandle methodDebugHandle = ((MethodDefinitionHandle)handle).ToDebugInformationHandle();
                LocalScopeHandleCollection localScopes = reader.GetLocalScopes(methodDebugHandle);
                foreach (LocalScopeHandle scopeHandle in localScopes)
                {
                    LocalScope scope = reader.GetLocalScope(scopeHandle);
                    LocalVariableHandleCollection localVars = scope.GetLocalVariables();
                    foreach (LocalVariableHandle varHandle in localVars)
                    {
                        LocalVariable localVar = reader.GetLocalVariable(varHandle);
                        if (localVar.Index == localIndex)
                        {
                            if (localVar.Attributes == LocalVariableAttributes.DebuggerHidden)
                                return false;

                            localVarName = reader.GetString(localVar.Name);
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        private static bool GetLocalsInfoForMethod(string assemblyPath, int methodToken, out List<LocalVarInfo> locals)
        {
            locals = null;

            OpenedReader openedReader = GetReader(assemblyPath, isFileLayout: true, peStream: null, pdbStream: null);
            if (openedReader == null)
                return false;

            using (openedReader)
            {
                try
                {
                    Handle handle = MetadataTokens.Handle(methodToken);
                    if (handle.Kind != HandleKind.MethodDefinition)
                        return false;

                    locals = new List<LocalVarInfo>();

                    MethodDebugInformationHandle methodDebugHandle =
                        ((MethodDefinitionHandle)handle).ToDebugInformationHandle();
                    LocalScopeHandleCollection localScopes = openedReader.Reader.GetLocalScopes(methodDebugHandle);
                    foreach (LocalScopeHandle scopeHandle in localScopes)
                    {
                        LocalScope scope = openedReader.Reader.GetLocalScope(scopeHandle);
                        LocalVariableHandleCollection localVars = scope.GetLocalVariables();
                        foreach (LocalVariableHandle varHandle in localVars)
                        {
                            LocalVariable localVar = openedReader.Reader.GetLocalVariable(varHandle);
                            if (localVar.Attributes == LocalVariableAttributes.DebuggerHidden)
                                continue;
                            LocalVarInfo info = new LocalVarInfo();
                            info.startOffset = scope.StartOffset;
                            info.endOffset = scope.EndOffset;
                            info.name = openedReader.Reader.GetString(localVar.Name);
                            locals.Add(info);
                        }
                    }
                }
                catch
                {
                    return false;
                }
            }
            return true;

        }

        /// <summary>
        /// Returns source name, line numbers and IL offsets for given method token. Used by the GDBJIT support.
        /// </summary>
        /// <param name="assemblyPath">file path of the assembly</param>
        /// <param name="methodToken">method token</param>
        /// <param name="debugInfo">structure with debug information return</param>
        /// <returns>true if information is available</returns>
        /// <remarks>used by the gdb JIT support (not SOS). Does not support in-memory PEs or PDBs</remarks>
        internal static bool GetInfoForMethod(string assemblyPath, int methodToken, ref MethodDebugInfo debugInfo)
        {
            try
            {
                List<DebugInfo> points = null;
                List<LocalVarInfo> locals = null;

                if (!GetDebugInfoForMethod(assemblyPath, methodToken, out points))
                {
                    return false;
                }

                if (!GetLocalsInfoForMethod(assemblyPath, methodToken, out locals))
                {
                    return false;
                }
                var structSize = Marshal.SizeOf<DebugInfo>();

                debugInfo.size = points.Count;
                var ptr = debugInfo.points;

                foreach (var info in points)
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    ptr = (IntPtr)(ptr.ToInt64() + structSize);
                }

                structSize = Marshal.SizeOf<LocalVarInfo>();

                debugInfo.localsSize = locals.Count;
                ptr = debugInfo.locals;

                foreach (var info in locals)
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    ptr = (IntPtr)(ptr.ToInt64() + structSize);
                }

                return true;
            }
            catch
            {
            }
            return false;
        }

        /// <summary>
        /// Helper method to return source name, line numbers and IL offsets for given method token.
        /// </summary>
        /// <param name="assemblyPath">file path of the assembly</param>
        /// <param name="methodToken">method token</param>
        /// <param name="points">list of debug information for each sequence point return</param>
        /// <returns>true if information is available</returns>
        /// <remarks>used by the gdb JIT support (not SOS). Does not support in-memory PEs or PDBs</remarks>
        private static bool GetDebugInfoForMethod(string assemblyPath, int methodToken, out List<DebugInfo> points)
        {
            points = null;

            OpenedReader openedReader = GetReader(assemblyPath, isFileLayout: true, peStream: null, pdbStream: null);
            if (openedReader == null)
                return false;

            using (openedReader)
            {
                try
                {
                    Handle handle = MetadataTokens.Handle(methodToken);
                    if (handle.Kind != HandleKind.MethodDefinition)
                        return false;

                    points = new List<DebugInfo>();
                    MethodDebugInformationHandle methodDebugHandle = ((MethodDefinitionHandle)handle).ToDebugInformationHandle();
                    MethodDebugInformation methodDebugInfo = openedReader.Reader.GetMethodDebugInformation(methodDebugHandle);
                    SequencePointCollection sequencePoints = methodDebugInfo.GetSequencePoints();

                    foreach (SequencePoint point in sequencePoints)
                    {

                        DebugInfo debugInfo = new DebugInfo();
                        debugInfo.lineNumber = point.StartLine;
                        debugInfo.fileName = openedReader.Reader.GetString(openedReader.Reader.GetDocument(point.Document).Name);
                        debugInfo.ilOffset = point.Offset;
                        points.Add(debugInfo);
                    }
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Returns the portable PDB reader for the assembly path
        /// </summary>
        /// <param name="assemblyPath">file path of the assembly or null if the module is in-memory or dynamic</param>
        /// <param name="isFileLayout">type of in-memory PE layout, if true, file based layout otherwise, loaded layout</param>
        /// <param name="peStream">in-memory PE stream</param>
        /// <param name="pdbStream">optional in-memory PDB stream</param>
        /// <returns>reader/provider wrapper instance</returns>
        /// <remarks>
        /// Assumes that neither PE image nor PDB loaded into memory can be unloaded or moved around.
        /// </remarks>
        private static OpenedReader GetReader(string assemblyPath, bool isFileLayout, Stream peStream, Stream pdbStream)
        {
            return (pdbStream != null) ? TryOpenReaderForInMemoryPdb(pdbStream) : TryOpenReaderFromAssembly(assemblyPath, isFileLayout, peStream);
        }

        private static OpenedReader TryOpenReaderForInMemoryPdb(Stream pdbStream)
        {
            Debug.Assert(pdbStream != null);

            byte[] buffer = new byte[sizeof(uint)];
            if (pdbStream.Read(buffer, 0, sizeof(uint)) != sizeof(uint))
            {
                return null;
            }
            uint signature = BitConverter.ToUInt32(buffer, 0);

            // quick check to avoid throwing exceptions below in common cases:
            const uint ManagedMetadataSignature = 0x424A5342;
            if (signature != ManagedMetadataSignature)
            {
                // not a Portable PDB
                return null;
            }

            OpenedReader result = null;
            MetadataReaderProvider provider = null;
            try
            {
                pdbStream.Position = 0;
                provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
                result = new OpenedReader(provider, provider.GetMetadataReader());
            }
            catch (Exception e) when (e is BadImageFormatException || e is IOException)
            {
                return null;
            }
            finally
            {
                if (result == null)
                {
                    provider?.Dispose();
                }
            }

            return result;
        }

        private static OpenedReader TryOpenReaderFromAssembly(string assemblyPath, bool isFileLayout, Stream peStream)
        {
            if (assemblyPath == null && peStream == null)
                return null;

            PEStreamOptions options = isFileLayout ? PEStreamOptions.Default : PEStreamOptions.IsLoadedImage;
            if (peStream == null)
            {
                peStream = TryOpenFile(assemblyPath);
                if (peStream == null)
                    return null;
                
                options = PEStreamOptions.Default;
            }

            try
            {
                using (var peReader = new PEReader(peStream, options))
                {
                    ReadPortableDebugTableEntries(peReader, out DebugDirectoryEntry codeViewEntry, out DebugDirectoryEntry embeddedPdbEntry);

                    // First try .pdb file specified in CodeView data (we prefer .pdb file on disk over embedded PDB
                    // since embedded PDB needs decompression which is less efficient than memory-mapping the file).
                    if (codeViewEntry.DataSize != 0)
                    {
                        var result = TryOpenReaderFromCodeView(peReader, codeViewEntry, assemblyPath);
                        if (result != null)
                        {
                            return result;
                        }
                    }

                    // if it failed try Embedded Portable PDB (if available):
                    if (embeddedPdbEntry.DataSize != 0)
                    {
                        return TryOpenReaderFromEmbeddedPdb(peReader, embeddedPdbEntry);
                    }
                }
            }
            catch (Exception e) when (e is BadImageFormatException || e is IOException)
            {
                // nop
            }

            return null;
        }

        private static void ReadPortableDebugTableEntries(PEReader peReader, out DebugDirectoryEntry codeViewEntry, out DebugDirectoryEntry embeddedPdbEntry)
        {
            // See spec: https://github.com/dotnet/corefx/blob/master/src/System.Reflection.Metadata/specs/PE-COFF.md

            codeViewEntry = default;
            embeddedPdbEntry = default;

            foreach (DebugDirectoryEntry entry in peReader.ReadDebugDirectory())
            {
                if (entry.Type == DebugDirectoryEntryType.CodeView)
                {
                    if (entry.MinorVersion != ImageDebugDirectory.PortablePDBMinorVersion)
                    {
                        continue;
                    }
                    codeViewEntry = entry;
                }
                else if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
                {
                    embeddedPdbEntry = entry;
                }
            }
        }

        private static OpenedReader TryOpenReaderFromCodeView(PEReader peReader, DebugDirectoryEntry codeViewEntry, string assemblyPath)
        {
            OpenedReader result = null;
            MetadataReaderProvider provider = null;
            try
            {
                CodeViewDebugDirectoryData data = peReader.ReadCodeViewDebugDirectoryData(codeViewEntry);
                string pdbPath = data.Path;
                Stream pdbStream = null;

                if (assemblyPath != null) 
                {
                    try
                    {
                        pdbPath = Path.Combine(Path.GetDirectoryName(assemblyPath), GetFileName(pdbPath));
                    }
                    catch
                    {
                        // invalid characters in CodeView path
                        return null;
                    }
                    pdbStream = TryOpenFile(pdbPath);
                }

                if (pdbStream == null)
                {
                    if (IsSymbolStoreEnabled())
                    {
                        Debug.Assert(codeViewEntry.MinorVersion == ImageDebugDirectory.PortablePDBMinorVersion);
                        SymbolStoreKey key = PortablePDBFileKeyGenerator.GetKey(pdbPath, data.Guid);
                        pdbStream = GetSymbolStoreFile(key)?.Stream;
                    }
                    if (pdbStream == null)
                    {
                        return null;
                    }
                    // Make sure the stream is at the beginning of the pdb.
                    pdbStream.Position = 0;
                }

                provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
                MetadataReader reader = provider.GetMetadataReader();

                // Validate that the PDB matches the assembly version
                if (data.Age == 1 && new BlobContentId(reader.DebugMetadataHeader.Id) == new BlobContentId(data.Guid, codeViewEntry.Stamp))
                {
                    result = new OpenedReader(provider, reader);
                }
            }
            catch (Exception e) when (e is BadImageFormatException || e is IOException)
            {
                return null;
            }
            finally
            {
                if (result == null)
                {
                    provider?.Dispose();
                }
            }

            return result;
        }

        private static OpenedReader TryOpenReaderFromEmbeddedPdb(PEReader peReader, DebugDirectoryEntry embeddedPdbEntry)
        {
            OpenedReader result = null;
            MetadataReaderProvider provider = null;

            try
            {
                // TODO: We might want to cache this provider globally (across stack traces), 
                // since decompressing embedded PDB takes some time.
                provider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embeddedPdbEntry);
                result = new OpenedReader(provider, provider.GetMetadataReader());
            }
            catch (Exception e) when (e is BadImageFormatException || e is IOException)
            {
                return null;
            }
            finally
            {
                if (result == null)
                {
                    provider?.Dispose();
                }
            }

            return result;
        }

        /// <summary>
        /// Returns true if symbol download has been enabled.
        /// </summary>
        public static bool IsSymbolStoreEnabled()
        {
            return s_symbolStore != null;
        }

        /// <summary>
        /// Attempts to download/retrieve from cache the key.
        /// </summary>
        /// <param name="key">index of the file to retrieve</param>
        /// <returns>stream or null</returns>
        internal static SymbolStoreFile GetSymbolStoreFile(SymbolStoreKey key)
        {
            try
            {
                return s_symbolStore.GetFile(key, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is BadImageFormatException || ex is IOException)
            {
                s_tracer.Error("Exception: {0}", ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// Parses the Windows debugger symbol path (srv*, cache*, etc.).
        /// </summary>
        /// <param name="store">symbol store to chain</param>
        /// <param name="symbolPath">Windows symbol path</param>
        /// <returns>if false, error parsing symbol path</returns>
        private static bool ParseSymbolPath(ref SymbolStore store, string symbolPath)
        {
            string[] paths = symbolPath.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string path in paths.Reverse())
            {
                string[] parts = path.Split(new char[] { '*' }, StringSplitOptions.RemoveEmptyEntries);

                // UNC or directory paths are ignored (paths not prefixed with srv* or cache*).
                if (parts.Length > 0)
                {
                    string symbolServerPath = null;
                    string symbolCachePath = null;
                    string symbolDirectoryPath = null;
                    bool msdl = false;

                    switch (parts[0].ToLowerInvariant())
                    {
                        case "srv":
                            switch (parts.Length)
                            { 
                                case 1:
                                    msdl = true;
                                    symbolCachePath = GetDefaultSymbolCache();
                                    break;
                                case 2:
                                    symbolServerPath = parts[1];
                                    break;
                                case 3:
                                    symbolCachePath = parts[1];
                                    symbolServerPath = parts[2];
                                    break;
                                default:
                                    return false;
                            }
                            break;

                        case "cache":
                            switch (parts.Length)
                            { 
                                case 1:
                                    symbolCachePath = GetDefaultSymbolCache();
                                    break;
                                case 2:
                                    symbolCachePath = parts[1];
                                    break;
                                default:
                                    return false;
                            }
                            break;

                        default:
                            // Directory path search
                            switch (parts.Length)
                            {
                                case 1:
                                    symbolDirectoryPath = parts[0];
                                    break;
                                default:
                                    return false;
                            }
                            break;
                    }

                    // Add the symbol stores to the chain
                    if (!GetServerSymbolStore(ref store, msdl, false, symbolServerPath, timeoutInMinutes: 0, symbolCachePath, symbolDirectoryPath))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool GetServerSymbolStore(ref SymbolStore store, bool msdl, bool symweb, string symbolServerPath, int timeoutInMinutes, string symbolCachePath, string symbolDirectoryPath)
        {
            bool internalServer = false;

            // Add symbol server URL if exists
            if (symbolServerPath == null)
            {
                if (msdl)
                {
                    symbolServerPath = MsdlSymbolServer;
                }
                else if (symweb)
                {
                    symbolServerPath = SymwebSymbolServer;
                    internalServer = true;
                }
            }
            else
            {
                // Use the internal symbol store for symweb
                internalServer = symbolServerPath.Contains("symweb");
            }

            if (symbolServerPath != null)
            {
                // Validate symbol server path
                if (!Uri.TryCreate(symbolServerPath.TrimEnd('/') + '/', UriKind.Absolute, out Uri uri))
                {
                    return false;
                }

                // Add a cache symbol store if file or UNC path
                if (uri.IsFile || uri.IsUnc)
                {
                    AddCachePath(ref store, symbolServerPath);
                }
                else
                {
                    if (!IsDuplicateSymbolStore<HttpSymbolStore>(store, (httpSymbolStore) => uri.Equals(httpSymbolStore.Uri)))
                    {
                        // Create http symbol server store
                        HttpSymbolStore httpSymbolStore;
                        if (internalServer)
                        {
                            httpSymbolStore = new SymwebHttpSymbolStore(s_tracer, store, uri);
                        }
                        else
                        {
                            httpSymbolStore = new HttpSymbolStore(s_tracer, store, uri);
                        }
                        if (timeoutInMinutes != 0)
                        {
                            httpSymbolStore.Timeout = TimeSpan.FromMinutes(timeoutInMinutes);
                        }
                        store = httpSymbolStore;
                    }
                }
            }

            if (symbolCachePath != null)
            {
                AddCachePath(ref store, symbolCachePath);
            }

            if (symbolDirectoryPath != null)
            {
                symbolDirectoryPath = Path.GetFullPath(symbolDirectoryPath);

                if (!IsDuplicateSymbolStore<DirectorySymbolStore>(store, (directorySymbolStore) => IsPathEqual(symbolDirectoryPath, directorySymbolStore.Directory)))
                {
                    store = new DirectorySymbolStore(s_tracer, store, symbolDirectoryPath);
                }
            }

            return true;
        }

        private static void AddCachePath(ref SymbolStore store, string symbolCachePath)
        {
            symbolCachePath = Path.GetFullPath(symbolCachePath);

            // Check only the first symbol store for duplication. The same cache directory can be
            // added more than once but just not more than once in a row.
            if (!(store is CacheSymbolStore cacheSymbolStore && IsPathEqual(symbolCachePath, cacheSymbolStore.CacheDirectory)))
            {
                store = new CacheSymbolStore(s_tracer, store, symbolCachePath);
            }
        }

        private static bool IsDuplicateSymbolStore<T>(SymbolStore symbolStore, Func<T, bool> match) 
            where T : SymbolStore
        {
            while (symbolStore != null)
            {
                if (symbolStore is T store)
                {
                    // TODO: replace this by adding an Equal override to the symbol stores
                    if (match(store))
                    {
                        return true;
                    }
                }
                symbolStore = symbolStore.BackingStore;
            }
            return false;
        }

        private static string GetDefaultSymbolCache()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(Path.GetTempPath(), "SymbolCache");
            }
            else
            {
                return Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".dotnet", "symbolcache");
            }
        }

        /// <summary>
        /// Attempt to open a file stream.
        /// </summary>
        /// <param name="path">file path</param>
        /// <returns>stream or null if doesn't exist or error</returns>
        internal static Stream TryOpenFile(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    return File.OpenRead(path);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is NotSupportedException || ex is IOException)
                {
                }
            }
            return null;
        }

        /// <summary>
        /// Compares two file paths using OS specific casing.
        /// </summary>
        private static bool IsPathEqual(string path1, string path2)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) 
            {
                return StringComparer.OrdinalIgnoreCase.Equals(path1, path2);
            }
            else 
            {
                return string.Equals(path1, path2);
            }
        }

        /// <summary>
        /// Quick fix for Path.GetFileName which incorrectly handles Windows-style paths on Linux
        /// </summary>
        /// <param name="pathName"> File path to be processed </param>
        /// <returns>Last component of path</returns>
        private static string GetFileName(string pathName)
        {
            int pos = pathName.LastIndexOfAny(new char[] { '/', '\\'});
            if (pos < 0)
            {
                return pathName;
            }
            return pathName.Substring(pos + 1);
        }
    }
}
