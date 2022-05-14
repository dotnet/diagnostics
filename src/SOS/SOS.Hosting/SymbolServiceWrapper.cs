﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
            WindowsDesktop  = 0,
            WindowsCore     = 1,
            UnixCore        = 2,
            OSXCore         = 3
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

        private readonly ISymbolService _symbolService;
        private readonly IMemoryService _memoryService;
        private readonly ulong _ignoreAddressBitsMask;

        public SymbolServiceWrapper(ISymbolService symbolService, IMemoryService memoryService)
        {
            Debug.Assert(symbolService != null);
            Debug.Assert(memoryService != null);
            _symbolService = symbolService;
            _memoryService = memoryService;
            _ignoreAddressBitsMask = memoryService.SignExtensionMask();
            Debug.Assert(_symbolService != null);

            VTableBuilder builder = AddInterface(IID_ISymbolService, validate: false);
            builder.AddMethod(new ParseSymbolPathDelegate(ParseSymbolPath));
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
        /// Parse the Windows sympath format
        /// </summary>
        /// <param name="symbolPath">windows symbol path</param>
        /// <returns>if false, failure</returns>
        private bool ParseSymbolPath(
            IntPtr self,
            string symbolPath)
        {
            if (string.IsNullOrWhiteSpace(symbolPath)) {
                return false;
            }
            _symbolService.DisableSymbolStore();
            return _symbolService.ParseSymbolPathFixDefault(symbolPath);
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
                ISymbolFile symbolFile = null;
                if (loadedPeAddress != 0)
                {
                    loadedPeAddress &= _ignoreAddressBitsMask;
                    Stream peStream = _memoryService.CreateMemoryStream(loadedPeAddress, loadedPeSize);
                    symbolFile = _symbolService.OpenSymbolFile(assemblyPath, isFileLayout, peStream);
                }
                if (inMemoryPdbAddress != 0)
                {
                    inMemoryPdbAddress &= _ignoreAddressBitsMask;
                    Stream pdbStream = _memoryService.CreateMemoryStream(inMemoryPdbAddress, inMemoryPdbSize);
                    symbolFile = _symbolService.OpenSymbolFile(pdbStream);
                }
                if (symbolFile != null)
                {
                    GCHandle gch = GCHandle.Alloc(symbolFile);
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
                if (gch.Target is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                gch.Free();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Dispose: {ex.Message}");
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
        private bool ResolveSequencePoint(
            IntPtr self,
            IntPtr symbolReaderHandle,
            string filePath,
            int lineNumber,
            out int methodToken,
            out int ilOffset)
        {
            Debug.Assert(symbolReaderHandle != IntPtr.Zero);
            GCHandle gch = GCHandle.FromIntPtr(symbolReaderHandle);
            ISymbolFile symbolFile = (ISymbolFile)gch.Target;
            return symbolFile.ResolveSequencePoint(filePath, lineNumber, out methodToken, out ilOffset);
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
            ISymbolFile symbolFile = (ISymbolFile)gch.Target;
            if (!symbolFile.GetSourceLineByILOffset(methodToken, ilOffset, out lineNumber, out string sourceFileName))
            {
                return false;
            }
            fileName = Marshal.StringToBSTR(sourceFileName);
            return true;
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
            ISymbolFile symbolFile = (ISymbolFile)gch.Target;
            if (!symbolFile.GetLocalVariableByIndex(methodToken, localIndex, out string localVar))
            {
                return false;
            }
            localVarName = Marshal.StringToBSTR(localVar);
            return true;
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
            return _symbolService.GetMetadataLocator(
                imagePath,
                imageTimestamp,
                imageSize,
                mvid, 
                mdRva,
                flags,
                bufferSize,
                pMetadata,
                pMetadataSize);
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
            return _symbolService.GetICorDebugMetadataLocator(
                imagePath,
                imageTimestamp,
                imageSize,
                pathBufferSize,
                pPathBufferSize,
                pwszPathBuffer);
        }

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
