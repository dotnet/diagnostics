// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SOS.Hosting
{
    public sealed class SymbolServiceWrapper : COMCallableIUnknown
    {
        /// <summary>
        /// Matches the IRuntime::RuntimeConfiguration in runtime.h
        /// </summary>
        public enum RuntimeConfiguration
        {
            WindowsDesktop = 0,
            WindowsCore = 1,
            UnixCore = 2,
            OSXCore = 3
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
        /// Writeline delegate for symbol store logging
        /// </summary>
        /// <param name="message"></param>
        private delegate void WriteLine([MarshalAs(UnmanagedType.LPStr)] string message);

        /// <summary>
        /// The LoadNativeSymbols callback
        /// </summary>
        /// <param name="moduleFileName">module file name</param>
        /// <param name="symbolFileName">symbol file name and path</param>
        private delegate void SymbolFileCallback(
            IntPtr parameter,
            [MarshalAs(UnmanagedType.LPStr)] string moduleFileName,
            [MarshalAs(UnmanagedType.LPStr)] string symbolFileName);

        public static readonly Guid IID_ISymbolService = new Guid("7EE88D46-F8B3-4645-AD3E-01FE7D4F70F1");

        private readonly Func<IMemoryService> _getMemoryService;
        private readonly ISymbolService _symbolService;

        public SymbolServiceWrapper(IHost host, Func<IMemoryService> getMemoryService)
        {
            Debug.Assert(host != null);
            Debug.Assert(getMemoryService != null);
            _getMemoryService = getMemoryService;
            _symbolService = host.Services.GetService<ISymbolService>();
            Debug.Assert(_symbolService != null);

            VTableBuilder builder = AddInterface(IID_ISymbolService, validate: false);
            builder.AddMethod(new IsSymbolStoreEnabledDelegate((IntPtr self) => _symbolService.IsSymbolStoreEnabled));
            builder.AddMethod(new InitializeSymbolStoreDelegate(InitializeSymbolStore));
            builder.AddMethod(new ParseSymbolPathDelegate(ParseSymbolPath));
            builder.AddMethod(new DisplaySymbolStoreDelegate(DisplaySymbolStore));
            builder.AddMethod(new DisableSymbolStoreDelegate((IntPtr self) => _symbolService.DisableSymbolStore()));
            builder.AddMethod(new LoadNativeSymbolsDelegate(LoadNativeSymbols));
            builder.AddMethod(new LoadNativeSymbolsFromIndexDelegate(LoadNativeSymbolsFromIndex));
            builder.AddMethod(new LoadSymbolsForModuleDelegate(LoadSymbolsForModule));
            builder.AddMethod(new DisposeDelegate(Dispose));
            builder.AddMethod(new ResolveSequencePointDelegate(ResolveSequencePoint));
            builder.AddMethod(new GetLocalVariableNameDelegate(GetLocalVariableName));
            builder.AddMethod(new GetLineByILOffsetDelegate(GetLineByILOffset));
            builder.AddMethod(new GetExpressionValueDelegate(GetExpressionValue));
            builder.AddMethod(new GetMetadataLocatorDelegate(GetMetadataLocator));
            builder.AddMethod(new GetICorDebugMetadataLocatorDelegate(GetICorDebugMetadataLocator));
            builder.Complete();

            AddRef();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("SymbolServiceWrapper.Destroy");
        }

        /// <summary>
        /// Initializes symbol loading. Adds the symbol server and/or the cache path (if not null) to the list of
        /// symbol servers. This API can be called more than once to add more servers to search.
        /// </summary>
        /// <param name="msdl">if true, use the public Microsoft server</param>
        /// <param name="symweb">if true, use symweb internal server and protocol (file.ptr)</param>
        /// <param name="symbolServerPath">symbol server url (optional)</param>
        /// <param name="timeoutInMinutes">symbol server timeout in minutes (optional)</param>
        /// <param name="symbolCachePath">symbol cache directory path (optional)</param>
        /// <param name="symbolDirectoryPath">symbol directory path to search (optional)</param>
        /// <returns>if false, failure</returns>
        private bool InitializeSymbolStore(
            IntPtr self,
            bool msdl,
            bool symweb,
            string symbolServerPath,
            string authToken,
            int timeoutInMinutes,
            string symbolCachePath,
            string symbolDirectoryPath)
        {
            if (msdl || symweb || symbolServerPath != null)
            {
                // Add the default symbol cache if no cache specified and adding server
                if (symbolCachePath == null)
                {
                    symbolCachePath = _symbolService.DefaultSymbolCache;
                }
                if (!_symbolService.AddSymbolServer(msdl, symweb, symbolServerPath, authToken, timeoutInMinutes))
                {
                    return false;
                }
            }
            if (symbolCachePath != null)
            {
                _symbolService.AddCachePath(symbolCachePath);
            }
            if (symbolDirectoryPath != null)
            {
                _symbolService.AddDirectoryPath(symbolDirectoryPath);
            }
            return true;
        }

        /// <summary>
        /// Parse the Windows sympath format
        /// </summary>
        /// <param name="symbolPath">windows symbol path</param>
        /// <returns>if false, failure</returns>
        private bool ParseSymbolPath(
            IntPtr self,
            string symbolPath)
        {
            if (string.IsNullOrWhiteSpace(symbolPath))
            {
                return false;
            }
            return _symbolService.ParseSymbolPathFixDefault(symbolPath);
        }

        /// <summary>
        /// Displays the symbol server and cache configuration
        /// </summary>
        private void DisplaySymbolStore(IntPtr self, WriteLine writeLine)
        {
            writeLine(_symbolService.ToString());
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
        private void LoadNativeSymbols(
            IntPtr self,
            SymbolFileCallback callback,
            IntPtr parameter,
            RuntimeConfiguration config,
            string moduleFilePath,
            ulong address,
            uint size)
        {
            if (_symbolService.IsSymbolStoreEnabled)
            {
                try
                {
                    KeyGenerator generator = null;
                    if (config == RuntimeConfiguration.UnixCore)
                    {
                        Stream stream = MemoryService.CreateMemoryStream();
                        var elfFile = new ELFFile(new StreamAddressSpace(stream), address, true);
                        generator = new ELFFileKeyGenerator(Tracer.Instance, elfFile, moduleFilePath);
                    }
                    else if (config == RuntimeConfiguration.OSXCore)
                    {
                        Stream stream = MemoryService.CreateMemoryStream();
                        var machOFile = new MachOFile(new StreamAddressSpace(stream), address, true);
                        generator = new MachOFileKeyGenerator(Tracer.Instance, machOFile, moduleFilePath);
                    }
                    else if (config == RuntimeConfiguration.WindowsCore || config == RuntimeConfiguration.WindowsDesktop)
                    {
                        Stream stream = MemoryService.CreateMemoryStream(address, size);
                        var peFile = new PEFile(new StreamAddressSpace(stream), true);
                        generator = new PEFileKeyGenerator(Tracer.Instance, peFile, moduleFilePath);
                    }
                    else
                    {
                        Trace.TraceError("LoadNativeSymbols: unsupported config {0}", config);
                    }
                    if (generator != null)
                    {
                        IEnumerable<SymbolStoreKey> keys = generator.GetKeys(KeyTypeFlags.SymbolKey | KeyTypeFlags.DacDbiKeys);
                        foreach (SymbolStoreKey key in keys)
                        {
                            string moduleFileName = Path.GetFileName(key.FullPathName);
                            Trace.TraceInformation("{0} {1}", key.FullPathName, key.Index);

                            string downloadFilePath = _symbolService.DownloadFile(key);
                            if (downloadFilePath != null)
                            {
                                Trace.TraceInformation("{0}: {1}", moduleFileName, downloadFilePath);
                                callback(parameter, moduleFileName, downloadFilePath);
                            }
                        }
                    }
                }
                catch (Exception ex) when
                   (ex is DiagnosticsException ||
                    ex is BadInputFormatException ||
                    ex is InvalidVirtualAddressException ||
                    ex is ArgumentOutOfRangeException ||
                    ex is IndexOutOfRangeException ||
                    ex is TaskCanceledException)
                {
                    Trace.TraceError("{0} address {1:X16}: {2}", moduleFilePath, address, ex.Message);
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
        private void LoadNativeSymbolsFromIndex(
            IntPtr self,
            SymbolFileCallback callback,
            IntPtr parameter,
            RuntimeConfiguration config,
            string moduleFilePath,
            bool specialKeys,
            int moduleIndexSize,
            IntPtr moduleIndex)
        {
            if (_symbolService.IsSymbolStoreEnabled)
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
                            Trace.TraceError("LoadNativeSymbolsFromIndex: unsupported platform {0}", config);
                            return;
                    }
                    foreach (SymbolStoreKey key in keys)
                    {
                        string moduleFileName = Path.GetFileName(key.FullPathName);
                        Trace.TraceInformation("{0} {1}", key.FullPathName, key.Index);

                        string downloadFilePath = _symbolService.DownloadFile(key);
                        if (downloadFilePath != null)
                        {
                            Trace.TraceInformation("{0}: {1}", moduleFileName, downloadFilePath);
                            callback(parameter, moduleFileName, downloadFilePath);
                        }
                    }
                }
                catch (Exception ex) when (ex is BadInputFormatException || ex is InvalidVirtualAddressException || ex is TaskCanceledException)
                {
                    Trace.TraceError("{0} - {1}", ex.Message, moduleFilePath);
                }
            }
        }

        /// <summary>
        /// Checks availability of debugging information for given assembly.
        /// </summary>
        /// <param name="assemblyPath">
        /// File path of the assembly or null
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
        private IntPtr LoadSymbolsForModule(
            IntPtr self,
            string assemblyPath,
            bool isFileLayout,
            ulong loadedPeAddress,
            uint loadedPeSize,
            ulong inMemoryPdbAddress,
            uint inMemoryPdbSize)
        {
            try
            {
                Stream peStream = null;
                if (loadedPeAddress != 0)
                {
                    peStream = MemoryService.CreateMemoryStream(loadedPeAddress, loadedPeSize);
                }
                Stream pdbStream = null;
                if (inMemoryPdbAddress != 0)
                {
                    pdbStream = MemoryService.CreateMemoryStream(inMemoryPdbAddress, inMemoryPdbSize);
                }
                OpenedReader openedReader = GetReader(assemblyPath, isFileLayout, peStream, pdbStream);
                if (openedReader != null)
                {
                    GCHandle gch = GCHandle.Alloc(openedReader);
                    return GCHandle.ToIntPtr(gch);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"LoadSymbolsForModule: {ex.Message}");
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Cleanup and dispose of symbol reader handle
        /// </summary>
        /// <param name="symbolReaderHandle">symbol reader handle returned by LoadSymbolsForModule</param>
        private void Dispose(
            IntPtr self,
            IntPtr symbolReaderHandle)
        {
            Debug.Assert(symbolReaderHandle != IntPtr.Zero);
            try
            {
                GCHandle gch = GCHandle.FromIntPtr(symbolReaderHandle);
                ((OpenedReader)gch.Target).Dispose();
                gch.Free();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Dispose: {ex.Message}");
            }
        }

        /// <summary>
        /// Get expression helper for native SOS.
        /// </summary>
        /// <param name="expression">hex number</param>
        /// <returns>value</returns>
        internal static ulong GetExpressionValue(
            IntPtr self,
            string expression)
        {
            if (expression != null)
            {
                if (ulong.TryParse(expression.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong result))
                {
                    return result;
                }
            }
            return 0;
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
        private bool ResolveSequencePoint(
            IntPtr self,
            IntPtr symbolReaderHandle,
            string filePath,
            int lineNumber,
            out int methodToken,
            out int ilOffset)
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
            catch (Exception ex)
            {
                Trace.TraceError($"ResolveSequencePoint: {ex.Message}");
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
        private bool GetLineByILOffset(
            IntPtr self,
            IntPtr symbolReaderHandle,
            int methodToken,
            long ilOffset,
            out int lineNumber,
            out IntPtr fileName)
        {
            Debug.Assert(symbolReaderHandle != IntPtr.Zero);
            fileName = IntPtr.Zero;

            GCHandle gch = GCHandle.FromIntPtr(symbolReaderHandle);
            OpenedReader openedReader = (OpenedReader)gch.Target;
            if (!GetSourceLineByILOffset(openedReader, methodToken, ilOffset, out lineNumber, out string sourceFileName))
            {
                return false;
            }
            fileName = Marshal.StringToBSTR(sourceFileName);
            return true;
        }

        /// <summary>
        /// Helper method to return source line number and source file name for given IL offset and method token.
        /// </summary>
        /// <param name="openedReader">symbol reader returned by LoadSymbolsForModule</param>
        /// <param name="methodToken">method token</param>
        /// <param name="ilOffset">IL offset</param>
        /// <param name="lineNumber">source line number return</param>
        /// <param name="fileName">source file name return</param>
        /// <returns> true if information is available</returns>
        private bool GetSourceLineByILOffset(
            OpenedReader openedReader,
            int methodToken,
            long ilOffset,
            out int lineNumber,
            out string fileName)
        {
            lineNumber = 0;
            fileName = null;
            MetadataReader reader = openedReader.Reader;
            try
            {
                Handle handle = MetadataTokens.Handle(methodToken);
                if (handle.Kind != HandleKind.MethodDefinition)
                {
                    return false;
                }

                MethodDebugInformationHandle methodDebugHandle = ((MethodDefinitionHandle)handle).ToDebugInformationHandle();
                if (methodDebugHandle.IsNil)
                {
                    return false;
                }

                MethodDebugInformation methodDebugInfo = reader.GetMethodDebugInformation(methodDebugHandle);
                SequencePointCollection sequencePoints = methodDebugInfo.GetSequencePoints();

                SequencePoint? nearestPoint = null;
                foreach (SequencePoint point in sequencePoints)
                {
                    if (point.Offset > ilOffset)
                    {
                        break;
                    }

                    if (point.StartLine != 0 && !point.IsHidden)
                    {
                        nearestPoint = point;
                    }
                }

                if (nearestPoint.HasValue)
                {
                    lineNumber = nearestPoint.Value.StartLine;
                    fileName = reader.GetString(reader.GetDocument(nearestPoint.Value.Document).Name);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"GetSourceLineByILOffset: {ex.Message}");
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
        private bool GetLocalVariableName(
            IntPtr self,
            IntPtr symbolReaderHandle,
            int methodToken,
            int localIndex,
            out IntPtr localVarName)
        {
            Debug.Assert(symbolReaderHandle != IntPtr.Zero);
            localVarName = IntPtr.Zero;

            GCHandle gch = GCHandle.FromIntPtr(symbolReaderHandle);
            OpenedReader openedReader = (OpenedReader)gch.Target;
            if (!GetLocalVariableByIndex(openedReader, methodToken, localIndex, out string localVar))
            {
                return false;
            }
            localVarName = Marshal.StringToBSTR(localVar);
            return true;
        }

        /// <summary>
        /// Helper method to return local variable name for given local index and IL offset.
        /// </summary>
        /// <param name="openedReader">symbol reader returned by LoadSymbolsForModule</param>
        /// <param name="methodToken">method token</param>
        /// <param name="localIndex">local variable index</param>
        /// <param name="localVarName">local variable name return</param>
        /// <returns>true if name has been found</returns>
        private bool GetLocalVariableByIndex(
            OpenedReader openedReader,
            int methodToken,
            int localIndex,
            out string localVarName)
        {
            localVarName = null;
            MetadataReader reader = openedReader.Reader;
            try
            {
                Handle handle = MetadataTokens.Handle(methodToken);
                if (handle.Kind != HandleKind.MethodDefinition)
                {
                    return false;
                }

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
                            {
                                return false;
                            }

                            localVarName = reader.GetString(localVar.Name);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"GetLocalVariableByIndex: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Metadata locator helper for the DAC.
        /// </summary>
        /// <param name="imagePath">file name and path to module</param>
        /// <param name="imageTimestamp">module timestamp</param>
        /// <param name="imageSize">module image</param>
        /// <param name="mvid">not used</param>
        /// <param name="mdRva">not used</param>
        /// <param name="flags">not used</param>
        /// <param name="bufferSize">size of incoming buffer (pMetadata)</param>
        /// <param name="pMetadata">pointer to buffer</param>
        /// <param name="pMetadataSize">size of outgoing metadata</param>
        /// <returns>HRESULT</returns>
        internal int GetMetadataLocator(
            IntPtr self,
            string imagePath,
            uint imageTimestamp,
            uint imageSize,
            byte[] mvid,
            uint mdRva,
            uint flags,
            uint bufferSize,
            IntPtr pMetadata,
            IntPtr pMetadataSize)
        {
            Debug.Assert(imageTimestamp != 0);
            Debug.Assert(imageSize != 0);

            if (pMetadata == IntPtr.Zero)
            {
                return HResult.E_INVALIDARG;
            }
            int hr = HResult.S_OK;
            int dataSize = 0;

            ImmutableArray<byte> metadata = _symbolService.GetMetadata(imagePath, imageTimestamp, imageSize);
            if (!metadata.IsEmpty)
            {
                dataSize = metadata.Length;
                int size = Math.Min((int)bufferSize, dataSize);
                Marshal.Copy(metadata.ToArray(), 0, pMetadata, size);
            }
            else
            {
                hr = HResult.E_FAIL;
            }

            if (pMetadataSize != IntPtr.Zero)
            {
                Marshal.WriteInt32(pMetadataSize, dataSize);
            }
            return hr;
        }

        /// <summary>
        /// Metadata locator helper for the DAC.
        /// </summary>
        /// <param name="imagePath">file name and path to module</param>
        /// <param name="imageTimestamp">module timestamp</param>
        /// <param name="imageSize">module image</param>
        /// <param name="pathBufferSize">output buffer size</param>
        /// <param name="pPathBufferSize">native pointer to put actual path size</param>
        /// <param name="pwszPathBuffer">native pointer to WCHAR path buffer</param>
        /// <returns>HRESULT</returns>
        internal int GetICorDebugMetadataLocator(
            IntPtr self,
            string imagePath,
            uint imageTimestamp,
            uint imageSize,
            uint pathBufferSize,
            IntPtr pPathBufferSize,
            IntPtr pwszPathBuffer)
        {
            return _symbolService.GetICorDebugMetadataLocator(imagePath, imageTimestamp, imageSize, pathBufferSize, pPathBufferSize, pwszPathBuffer);
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
        private OpenedReader GetReader(string assemblyPath, bool isFileLayout, Stream peStream, Stream pdbStream)
        {
            return (pdbStream != null) ? TryOpenReaderForInMemoryPdb(pdbStream) : TryOpenReaderFromAssembly(assemblyPath, isFileLayout, peStream);
        }

        private OpenedReader TryOpenReaderForInMemoryPdb(Stream pdbStream)
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

        private OpenedReader TryOpenReaderFromAssembly(string assemblyPath, bool isFileLayout, Stream peStream)
        {
            if (assemblyPath == null && peStream == null)
            {
                return null;
            }

            PEStreamOptions options = isFileLayout ? PEStreamOptions.Default : PEStreamOptions.IsLoadedImage;
            if (peStream == null)
            {
                peStream = TryOpenFile(assemblyPath);
                if (peStream == null)
                {
                    return null;
                }

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

        private void ReadPortableDebugTableEntries(PEReader peReader, out DebugDirectoryEntry codeViewEntry, out DebugDirectoryEntry embeddedPdbEntry)
        {
            // See spec: https://github.com/dotnet/runtime/blob/main/docs/design/specs/PE-COFF.md

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

        private OpenedReader TryOpenReaderFromCodeView(PEReader peReader, DebugDirectoryEntry codeViewEntry, string assemblyPath)
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
                    if (_symbolService.IsSymbolStoreEnabled)
                    {
                        Debug.Assert(codeViewEntry.MinorVersion == ImageDebugDirectory.PortablePDBMinorVersion);
                        SymbolStoreKey key = PortablePDBFileKeyGenerator.GetKey(pdbPath, data.Guid);
                        pdbStream = _symbolService.GetSymbolStoreFile(key)?.Stream;
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

        private OpenedReader TryOpenReaderFromEmbeddedPdb(PEReader peReader, DebugDirectoryEntry embeddedPdbEntry)
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
        /// Attempt to open a file stream.
        /// </summary>
        /// <param name="path">file path</param>
        /// <returns>stream or null if doesn't exist or error</returns>
        private Stream TryOpenFile(string path)
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
        /// Quick fix for Path.GetFileName which incorrectly handles Windows-style paths on Linux
        /// </summary>
        /// <param name="pathName"> File path to be processed </param>
        /// <returns>Last component of path</returns>
        private static string GetFileName(string pathName)
        {
            int pos = pathName.LastIndexOfAny(new char[] { '/', '\\' });
            if (pos < 0)
            {
                return pathName;
            }
            return pathName.Substring(pos + 1);
        }

        private IMemoryService MemoryService => _getMemoryService() ?? throw new DiagnosticsException("SymbolServiceWrapper: no current target");

        #region Symbol service delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate bool IsSymbolStoreEnabledDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate bool InitializeSymbolStoreDelegate(
            [In] IntPtr self,
            [In] bool msdl,
            [In] bool symweb,
            [In] string symbolServerPath,
            [In] string authToken,
            [In] int timeoutInMinutes,
            [In] string symbolCachePath,
            [In] string symbolDirectoryPath);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate bool ParseSymbolPathDelegate(
            [In] IntPtr self,
            [In] string windowsSymbolPath);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void DisplaySymbolStoreDelegate(
            [In] IntPtr self,
            [In] WriteLine writeLine);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void DisableSymbolStoreDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void LoadNativeSymbolsDelegate(
            [In] IntPtr self,
            [In] SymbolFileCallback callback,
            [In] IntPtr parameter,
            [In] RuntimeConfiguration config,
            [In] string moduleFilePath,
            [In] ulong address,
            [In] uint size);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void LoadNativeSymbolsFromIndexDelegate(
            [In] IntPtr self,
            [In] SymbolFileCallback callback,
            [In] IntPtr parameter,
            [In] RuntimeConfiguration config,
            [In] string moduleFilePath,
            [In] bool specialKeys,
            [In] int moduleIndexSize,
            [In] IntPtr moduleIndex);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr LoadSymbolsForModuleDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPWStr)] string assemblyPath,
            [In] bool isFileLayout,
            [In] ulong loadedPeAddress,
            [In] uint loadedPeSize,
            [In] ulong inMemoryPdbAddress,
            [In] uint inMemoryPdbSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void DisposeDelegate(
            [In] IntPtr self,
            [In] IntPtr symbolReaderHandle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate bool ResolveSequencePointDelegate(
            [In] IntPtr self,
            [In] IntPtr symbolReaderHandle,
            [In] string filePath,
            [In] int lineNumber,
            [Out] out int methodToken,
            [Out] out int ilOffset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate bool GetLineByILOffsetDelegate(
            [In] IntPtr self,
            [In] IntPtr symbolReaderHandle,
            [In] int methodToken,
            [In] long ilOffset,
            [Out] out int lineNumber,
            [Out] out IntPtr fileName);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate bool GetLocalVariableNameDelegate(
            [In] IntPtr self,
            [In] IntPtr symbolReaderHandle,
            [In] int methodToken,
            [In] int localIndex,
            [Out] out IntPtr localVarName);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate ulong GetExpressionValueDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPStr)] string expression);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetMetadataLocatorDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPWStr)] string imagePath,
            [In] uint imageTimestamp,
            [In] uint imageSize,
            [In, MarshalAs(UnmanagedType.LPArray, SizeConst = 16)] byte[] mvid,
            [In] uint mdRva,
            [In] uint flags,
            [In] uint bufferSize,
            [Out] IntPtr buffer,
            [Out] IntPtr dataSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetICorDebugMetadataLocatorDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPWStr)] string imagePath,
            [In] uint imageTimestamp,
            [In] uint imageSize,
            [In] uint pathBufferSize,
            [Out] IntPtr pPathBufferSize,
            [Out] IntPtr pPathBuffer);

        #endregion
    }
}
