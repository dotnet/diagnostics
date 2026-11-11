// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace SOS.Hosting.Loader
{
    internal sealed class SOSLoadContext : AssemblyLoadContext
    {
        private readonly string _directory;
        private readonly string _frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);

        public SOSLoadContext(string directory)
            : base("SOS", isCollectible: false)
        {
            _directory = directory;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            Assembly coreLibrary = typeof(object).Assembly;
            if (string.Equals(assemblyName.Name, coreLibrary.GetName().Name, StringComparison.OrdinalIgnoreCase))
            {
                return coreLibrary;
            }

            string fileName = assemblyName.Name + ".dll";
            string localPath = Path.Combine(_directory, fileName);
            string frameworkPath = Path.Combine(_frameworkDirectory, fileName);
            Version localVersion = File.Exists(localPath) ? AssemblyName.GetAssemblyName(localPath).Version : null;

            if (File.Exists(frameworkPath))
            {
                AssemblyName frameworkName = AssemblyName.GetAssemblyName(frameworkPath);
                if (frameworkName.Version >= assemblyName.Version &&
                    (localVersion is null || frameworkName.Version >= localVersion))
                {
                    // Framework assemblies also need SOS's binding policy for dependencies such as ImmutableArray.
                    return LoadFromAssemblyPath(frameworkPath);
                }
            }
            if (localVersion is not null)
            {
                return LoadFromAssemblyPath(localPath);
            }

            // Falling back to Default would bind SOS to unrelated debugger extensions.
            throw new FileNotFoundException($"SOS dependency {assemblyName} was not found in {_directory} or {_frameworkDirectory}.");
        }
    }
}
