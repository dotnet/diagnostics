// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.FileFormats.PE;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public static class Utilities
    { 
        /// <summary>
        /// An empty Version instance.
        /// </summary>
        public static readonly Version EmptyVersion = new();

        /// <summary>
        /// Format a immutable array of bytes into hex (i.e build id).
        /// </summary>
        public static string ToHex(this ImmutableArray<byte> array) => string.Concat(array.Select((b) => b.ToString("x2")));

        /// <summary>
        /// Combines two hash codes into a single hash code, in an order-dependent manner.
        /// </summary>
        /// <remarks>
        /// This function is neither commutative nor associative; the hash codes must be combined in
        /// a deterministic order.  Do not use this when hashing collections whose contents are
        /// nondeterministically ordered!
        /// </remarks>
        public static int CombineHashCodes(int hashCode0, int hashCode1)
        {
            unchecked {
                // This specific hash function is based on the Boost C++ library's CombineHash function:
                // http://stackoverflow.com/questions/4948780/magic-numbers-in-boosthash-combine
                // http://www.boost.org/doc/libs/1_46_1/doc/html/hash/combine.html 
                return hashCode0 ^ (hashCode1 + (int) 0x9e3779b9 + (hashCode0 << 6) + (hashCode0 >> 2));
            }
        }

        /// <summary>
        /// Convert from symstore VsFixedFileInfo to DebugServices VersionData
        /// </summary>
        public static Version ToVersion(this VsFixedFileInfo fileInfo)
        { 
            return new Version(fileInfo.FileVersionMajor, fileInfo.FileVersionMinor, fileInfo.FileVersionBuild, fileInfo.FileVersionRevision);
        }

        /// <summary>
        /// Convert from symstore PEPdbRecord to DebugServices PdbFileInfo
        /// </summary>
        public static PdbFileInfo ToPdbFileInfo(this PEPdbRecord pdbInfo)
        {
            return new PdbFileInfo(pdbInfo.Path, pdbInfo.Signature, pdbInfo.Age, pdbInfo.IsPortablePDB);
        }

        /// <summary>
        /// Opens and returns an PEReader instance from the local file path
        /// </summary>
        /// <param name="filePath">PE file to open</param>
        /// <returns>PEReader instance or null</returns>
        public static PEReader OpenPEReader(string filePath)
        {
            Stream stream = TryOpenFile(filePath);
            if (stream is not null)
            {
                try
                {
                    var reader = new PEReader(stream);
                    if (reader.PEHeaders == null || reader.PEHeaders.PEHeader == null)
                    {
                        Trace.TraceWarning($"OpenPEReader: PEReader invalid headers");
                        return null;
                    }
                    return reader;
                }
                catch (Exception ex) when (ex is BadImageFormatException || ex is IOException)
                {
                    Trace.TraceError($"OpenPEReader: PEReader exception {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Attempt to open a file stream.
        /// </summary>
        /// <param name="path">file path</param>
        /// <returns>stream or null if doesn't exist or error</returns>
        public static Stream TryOpenFile(string path)
        {
            if (path is not null && File.Exists(path))
            {
                try
                {
                    return File.OpenRead(path);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is NotSupportedException || ex is IOException)
                {
                    Trace.TraceError($"TryOpenFile: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// Call the constructor of the type and return the instance binding any
        /// services in the constructor parameters.
        /// </summary>
        /// <param name="type">type to create</param>
        /// <param name="provider">services</param>
        /// <param name="optional">if true, the service is not required</param>
        /// <returns>type instance</returns>
        public static object InvokeConstructor(Type type, IServiceProvider provider, bool optional)
        {
            ConstructorInfo constructor = type.GetConstructors().Single();
            object[] arguments = BuildArguments(constructor, provider, optional);
            try
            {
                return constructor.Invoke(arguments);
            }
            catch (TargetInvocationException ex)
            {
                Trace.TraceError(ex.ToString());
                throw ex.InnerException;
            }
        }

        /// <summary>
        /// Call the method and bind any services in the constructor parameters.
        /// </summary>
        /// <param name="method">method to invoke</param>
        /// <param name="instance">class instance or null if static</param>
        /// <param name="provider">services</param>
        /// <param name="optional">if true, the service is not required</param>
        /// <returns>method return value</returns>
        public static object Invoke(MethodBase method, object instance, IServiceProvider provider, bool optional)
        {
            object[] arguments = BuildArguments(method, provider, optional);
            try
            {
                return method.Invoke(instance, arguments);
            }
            catch (TargetInvocationException ex)
            {
                Trace.TraceError(ex.ToString());
                throw ex.InnerException;
            }
        }

        private static object[] BuildArguments(MethodBase methodBase, IServiceProvider services, bool optional)
        {
            ParameterInfo[] parameters = methodBase.GetParameters();
            object[] arguments = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                // The parameter will passed as null to allow for "optional" services. The invoked 
                // method needs to check for possible null parameters.
                arguments[i] = services.GetService(parameters[i].ParameterType);
                if (arguments[i] is null && !optional)
                {
                    throw new DiagnosticsException($"The {parameters[i].ParameterType} service is required by the {parameters[i].Name} parameter");
                }
            }
            return arguments;
        }
    }
}
