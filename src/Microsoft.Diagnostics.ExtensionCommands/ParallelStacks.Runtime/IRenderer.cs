// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ParallelStacks.Runtime
{
    /// <summary>
    /// The method of this interface are called to render each part of the parallel call stacks
    /// </summary>
    /// <remarks>
    /// Each method is responsible for adding color, tags or decoration on each element of the parallel stacks
    /// </remarks>
    public interface IRenderer
    {
        /// <summary>
        /// Max number of thread IDs to display at the end of each stack frame group.
        /// This is important in the case of 100+ threads applications.
        /// </summary>
        /// <remarks>
        /// Use -1 if there should not be any limit.
        /// </remarks>
        int DisplayThreadIDsCountLimit { get; }

        /// <summary>
        /// Render empty line
        /// </summary>
        /// <param name="text"></param>
        void Write(string text);

        /// <summary>
        /// Render count at the beginning of each line
        /// </summary>
        /// <param name="count"></param>
        void WriteCount(string count);

        /// <summary>
        /// Render namespace of each method type
        /// </summary>
        /// <param name="ns"></param>
        void WriteNamespace(string ns);

        /// <summary>
        /// Render each type in method signatures
        /// </summary>
        /// <param name="type"></param>
        void WriteType(string type);

        /// <summary>
        /// Render separators such as ( and .
        /// </summary>
        /// <param name="separator"></param>
        void WriteSeparator(string separator);

        /// <summary>
        /// Render dark signature element such as ByRef
        /// </summary>
        /// <param name="separator"></param>
        void WriteDark(string separator);

        /// <summary>
        /// Render method name
        /// </summary>
        /// <param name="method"></param>
        void WriteMethod(string method);

        /// <summary>
        /// Render method type (not including namespace)
        /// </summary>
        /// <param name="type"></param>
        void WriteMethodType(string type);

        /// <summary>
        /// Render separator between different stack frame blocks
        /// </summary>
        /// <param name="text"></param>
        void WriteFrameSeparator(string text);

        /// <summary>
        /// Render a thread id that will appear for each stack frames group (at the end of WriteFrameSeparator)
        /// For example, in HTML it could be used to add a link to show details such as ClrStack -p
        /// </summary>
        /// <param name="threadID"></param>
        string FormatTheadId(uint threadID);
    }
}
