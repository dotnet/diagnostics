// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    internal sealed class SymbolFile : ISymbolFile, IDisposable
    {
        private readonly MetadataReaderProvider _provider;
        private readonly MetadataReader _reader;

        public SymbolFile(MetadataReaderProvider provider, MetadataReader reader)
        {
            Debug.Assert(provider != null);
            Debug.Assert(reader != null);

            _provider = provider;
            _reader = reader;
        }

        public void Dispose() => _provider.Dispose();

        /// <summary>
        /// Returns method token and IL offset for given source line number.
        /// </summary>
        /// <param name="filePath">source file name and path</param>
        /// <param name="lineNumber">source line number</param>
        /// <param name="methodToken">method token return</param>
        /// <param name="ilOffset">IL offset return</param>
        /// <returns>true if information is available</returns>
        public bool ResolveSequencePoint(
            string filePath,
            int lineNumber,
            out int methodToken,
            out int ilOffset)
        {
            methodToken = 0;
            ilOffset = 0;
            try
            {
                string fileName = SymbolService.GetFileName(filePath);
                foreach (MethodDebugInformationHandle methodDebugInformationHandle in _reader.MethodDebugInformation)
                {
                    MethodDebugInformation methodDebugInfo = _reader.GetMethodDebugInformation(methodDebugInformationHandle);
                    SequencePointCollection sequencePoints = methodDebugInfo.GetSequencePoints();
                    foreach (SequencePoint point in sequencePoints)
                    {
                        string sourceName = _reader.GetString(_reader.GetDocument(point.Document).Name);
                        if (point.StartLine == lineNumber && SymbolService.GetFileName(sourceName) == fileName)
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
        /// <param name="methodToken">method token</param>
        /// <param name="ilOffset">IL offset</param>
        /// <param name="lineNumber">source line number return</param>
        /// <param name="fileName">source file name return</param>
        /// <returns>true if information is available</returns>
        public bool GetSourceLineByILOffset(
            int methodToken,
            long ilOffset,
            out int lineNumber,
            out string fileName)
        {
            lineNumber = 0;
            fileName = null;
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

                MethodDebugInformation methodDebugInfo = _reader.GetMethodDebugInformation(methodDebugHandle);
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
                    fileName = _reader.GetString(_reader.GetDocument(nearestPoint.Value.Document).Name);
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
        /// <param name="methodToken">method token</param>
        /// <param name="localIndex">local variable index</param>
        /// <param name="localVarName">local variable name return</param>
        /// <returns>true if name has been found</returns>
        public bool GetLocalVariableByIndex(
            int methodToken,
            int localIndex,
            out string localVarName)
        {
            localVarName = null;
            try
            {
                Handle handle = MetadataTokens.Handle(methodToken);
                if (handle.Kind != HandleKind.MethodDefinition)
                {
                    return false;
                }

                MethodDebugInformationHandle methodDebugHandle = ((MethodDefinitionHandle)handle).ToDebugInformationHandle();
                LocalScopeHandleCollection localScopes = _reader.GetLocalScopes(methodDebugHandle);
                foreach (LocalScopeHandle scopeHandle in localScopes)
                {
                    LocalScope scope = _reader.GetLocalScope(scopeHandle);
                    LocalVariableHandleCollection localVars = scope.GetLocalVariables();
                    foreach (LocalVariableHandle varHandle in localVars)
                    {
                        LocalVariable localVar = _reader.GetLocalVariable(varHandle);
                        if (localVar.Index == localIndex)
                        {
                            if (localVar.Attributes == LocalVariableAttributes.DebuggerHidden)
                            {
                                return false;
                            }

                            localVarName = _reader.GetString(localVar.Name);
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
    }
}
