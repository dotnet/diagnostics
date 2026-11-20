// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;

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
        /// Returns the pointer size for a given processor type
        /// </summary>
        /// <param name="architecture">processor type</param>
        /// <returns>pointer size</returns>
        /// <exception cref="NotSupportedException"></exception>
        public static int GetPointerSizeFromArchitecture(Architecture architecture)
        {
            switch (architecture)
            {
                case Architecture.X64:
                case Architecture.Arm64:
                case (Architecture)6 /* Architecture.LoongArch64 */:
                case (Architecture)9 /* Architecture.RiscV64 */:
                    return 8;
                case Architecture.X86:
                case Architecture.Arm:
                    return 4;
                default:
                    throw new NotSupportedException("Architecture not supported");
            }
        }

        /// <summary>
        /// Create an IModule from a managed ClrModule
        /// </summary>
        /// <param name="moduleService">module service</param>
        /// <param name="moduleIndex">module index or -1 if none</param>
        /// <param name="module">ClrModule instance</param>
        /// <returns></returns>
        public static IModule CreateModule(this IModuleService moduleService, int moduleIndex, ClrModule module)
        {
            ulong size = module.Size > 0 ? module.Size : 4096;
            return moduleService.CreateModule(moduleIndex, module.ImageBase, size, module.Name);
        }

        /// <summary>
        /// Combines two hash codes into a single hash code, in an order-dependent manner.
        /// </summary>
        /// <remarks>
        /// This function is neither commutative nor associative; the hash codes must be combined in
        /// a deterministic order.  Do not use this when hashing collections whose contents are
        /// non-deterministically ordered!
        /// </remarks>
        public static int CombineHashCodes(int hashCode0, int hashCode1)
        {
            unchecked
            {
                // This specific hash function is based on the Boost C++ library's CombineHash function:
                // http://stackoverflow.com/questions/4948780/magic-numbers-in-boosthash-combine
                // http://www.boost.org/doc/libs/1_46_1/doc/html/hash/combine.html
                return hashCode0 ^ (hashCode1 + (int)0x9e3779b9 + (hashCode0 << 6) + (hashCode0 >> 2));
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
        /// Helper function to that parses the version out of the version string that looks something
        /// like "8.0.23.10701 @Commit: e71a4fb10d7ea6b502dd5efe7a8fcefa2b9c1550"
        /// </summary>
        public static Version ParseVersionString(string versionString)
        {
            if (versionString != null)
            {
                int spaceIndex = versionString.IndexOf(' ');
                if (spaceIndex < 0)
                {
                    // It is probably a private build version that doesn't end with a space (no commit id after)
                    spaceIndex = versionString.Length;
                }
                if (spaceIndex > 0)
                {
                    if (versionString[spaceIndex - 1] == '.')
                    {
                        spaceIndex--;
                    }
                    string versionToParse = versionString.Substring(0, spaceIndex);
                    try
                    {
                        return Version.Parse(versionToParse);
                    }
                    catch (ArgumentException ex)
                    {
                        Trace.TraceError($"ParseVersionString FAILURE: '{versionToParse}' '{versionString}' {ex}");
                    }
                }
            }
            return null;
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
                    PEReader reader = new(stream);
                    if (reader.PEHeaders == null || reader.PEHeaders.PEHeader == null)
                    {
                        Trace.TraceWarning($"OpenPEReader: PEReader invalid headers");
                        return null;
                    }
                    return reader;
                }
                catch (Exception ex) when (ex is BadImageFormatException or IOException)
                {
                    Trace.TraceError($"OpenPEReader: PEReader exception {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Opens and returns an ELFFile instance from the local file path
        /// </summary>
        /// <param name="filePath">ELF file to open</param>
        /// <returns>ELFFile instance or null</returns>
        public static ELFFile OpenELFFile(string filePath)
        {
            Stream stream = TryOpenFile(filePath);
            if (stream is not null)
            {
                try
                {
                    ELFFile elfFile = new(new StreamAddressSpace(stream), position: 0, isDataSourceVirtualAddressSpace: false);
                    if (!elfFile.IsValid())
                    {
                        Trace.TraceError($"OpenFile: not a valid file {filePath}");
                        return null;
                    }
                    return elfFile;
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException or BadInputFormatException or IOException)
                {
                    Trace.TraceError($"OpenFile: {filePath} exception {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Opens and returns an MachOFile instance from the local file path
        /// </summary>
        /// <param name="filePath">MachO file to open</param>
        /// <returns>MachOFile instance or null</returns>
        public static MachOFile OpenMachOFile(string filePath)
        {
            Stream stream = TryOpenFile(filePath);
            if (stream is not null)
            {
                try
                {
                    MachOFile machoModule = new(new StreamAddressSpace(stream), position: 0, dataSourceIsVirtualAddressSpace: false);
                    if (!machoModule.IsValid())
                    {
                        Trace.TraceError($"OpenMachOFile: not a valid file {filePath}");
                        return null;
                    }
                    return machoModule;
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException or BadInputFormatException or IOException)
                {
                    Trace.TraceError($"OpenMachOFile: {filePath} exception {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Creates a ELFFile service instance of the module in memory.
        /// </summary>
        public static ELFFile CreateELFFile(IMemoryService memoryService, IModule module)
        {
            if (module.Target.OperatingSystem == OSPlatform.Linux)
            {
                Stream stream = memoryService.CreateMemoryStream();
                ELFFile elfFile = new(new StreamAddressSpace(stream), module.ImageBase, true);
                if (elfFile.IsValid())
                {
                    return elfFile;
                }
            }
            return null;
        }

        /// <summary>
        /// Creates a MachOFile service instance of the module in memory.
        /// </summary>
        public static MachOFile CreateMachOFile(IMemoryService memoryService, IModule module)
        {
            if (module.Target.OperatingSystem == OSPlatform.OSX)
            {
                Stream stream = memoryService.CreateMemoryStream();
                MachOFile elfFile = new(new StreamAddressSpace(stream), module.ImageBase, true);
                if (elfFile.IsValid())
                {
                    return elfFile;
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
                catch (Exception ex) when (ex is UnauthorizedAccessException or NotSupportedException or IOException)
                {
                    Trace.TraceError($"TryOpenFile: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the .NET user directory
        /// </summary>
        public static string GetDotNetHomeDirectory()
        {
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(basePath))
            {
                throw new ArgumentNullException("UserProfile directory not found");
            }

            return Path.Combine(basePath, ".dotnet");
        }

        /// <summary>
        /// Call a method with parameters resolved from the service provider or their declared defaults.
        /// </summary>
        public static object Invoke(MethodBase method, object instance, IServiceProvider provider)
        {
            object[] arguments = BuildArguments(method, provider);
            try
            {
                return method.Invoke(instance, arguments);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                Trace.TraceError(ex.ToString());
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static object[] BuildArguments(MethodBase method, IServiceProvider services)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                return Array.Empty<object>();
            }
            object[] arguments = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                object argument = parameter.ParameterType == typeof(IServiceProvider) ? services : services.GetService(parameter.ParameterType);
                if (argument is null)
                {
                    if (!parameter.HasDefaultValue)
                    {
                        throw new DiagnosticsException($"The {parameter.ParameterType} service is required by the {parameter.Name} parameter of {method.DeclaringType}.{method.Name}");
                    }
                    argument = parameter.DefaultValue;
                }
                arguments[i] = argument;
            }
            return arguments;
        }
    }
}
