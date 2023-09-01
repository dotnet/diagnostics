// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DebugServices
{
    public interface ISymbolFile
    {
        /// <summary>
        /// Returns method token and IL offset for given source line number.
        /// </summary>
        /// <param name="filePath">source file name and path</param>
        /// <param name="lineNumber">source line number</param>
        /// <param name="methodToken">method token return</param>
        /// <param name="ilOffset">IL offset return</param>
        /// <returns>true if information is available</returns>
        bool ResolveSequencePoint(string filePath, int lineNumber, out int methodToken, out int ilOffset);

        /// <summary>
        /// Returns source line number and source file name for given IL offset and method token.
        /// </summary>
        /// <param name="methodToken">method token</param>
        /// <param name="ilOffset">IL offset</param>
        /// <param name="lineNumber">source line number return</param>
        /// <param name="fileName">source file name return</param>
        /// <returns>true if information is available</returns>
        bool GetSourceLineByILOffset(int methodToken, long ilOffset, out int lineNumber, out string fileName);

        /// <summary>
        /// Returns local variable name for given local index and IL offset.
        /// </summary>
        /// <param name="methodToken">method token</param>
        /// <param name="localIndex">local variable index</param>
        /// <param name="localVarName">local variable name return</param>
        /// <returns>true if name has been found</returns>
        bool GetLocalVariableByIndex(int methodToken, int localIndex, out string localVarName);
    }
}
