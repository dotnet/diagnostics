// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using SOS.Hosting.Loader;

namespace SOS.Cohost
{
    public static class Host
    {
        public static int Initialize(string dependencyPath)
        {
            Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dependencyPath);
            if (AssemblyLoadContext.GetLoadContext(assembly) != AssemblyLoadContext.Default ||
                assembly.Location != dependencyPath)
            {
                throw new InvalidOperationException("The competing dependency must load into Default.");
            }
            return assembly.GetName().Version.Major;
        }

        public static int VerifyHostedSOS(string dependencyPath)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            SOSLoadContext context = (SOSLoadContext)AssemblyLoadContext.All.Single(candidate => candidate.Name == "SOS");
            string directory = Path.GetDirectoryName(context.Assemblies.Single(assembly => assembly.GetName().Name == "SOS.Extensions").Location);
            Verify(new[] { directory, dependencyPath, Environment.GetEnvironmentVariable("DOTNET_DIAGNOSTIC_EXTENSIONS") }, context);
            return Initialize(dependencyPath);
        }

        private static int Main(string[] args) => Verify(args);

        private static int Verify(string[] args, SOSLoadContext context = null)
        {
            string directory = args[0];
            string dependencyPath = args[1];
            int conflictVersion = Initialize(dependencyPath);
            Console.WriteLine($"Default loaded first: {dependencyPath}, version {conflictVersion}");

            context ??= new SOSLoadContext(directory);
            if (context.IsCollectible)
            {
                throw new InvalidOperationException("Native SOS interfaces require a noncollectible context.");
            }
            Assembly clrmd = context.LoadFromAssemblyName(new AssemblyName("Microsoft.Diagnostics.Runtime"));
            if (AssemblyLoadContext.GetLoadContext(clrmd) != context ||
                clrmd.Location != Path.Combine(directory, "Microsoft.Diagnostics.Runtime.dll"))
            {
                throw new InvalidOperationException("SOS bound to the competing ClrMD.");
            }
            Type dataTarget = clrmd.GetType("Microsoft.Diagnostics.Runtime.DataTarget", throwOnError: true);
            Console.WriteLine($"SOS loaded: {dataTarget.Assembly.FullName}, context {context.Name}");
            Assembly extensions = context.LoadFromAssemblyName(new AssemblyName("SOS.Extensions"));
            extensions.GetTypes();

            string frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
            foreach (string localPath in Directory.EnumerateFiles(directory, "*.dll"))
            {
                string frameworkPath = Path.Combine(frameworkDirectory, Path.GetFileName(localPath));
                if (!File.Exists(frameworkPath))
                {
                    continue;
                }
                AssemblyName localName = AssemblyName.GetAssemblyName(localPath);
                Version frameworkVersion = AssemblyName.GetAssemblyName(frameworkPath).Version;
                Assembly selected = context.LoadFromAssemblyName(localName);
                string expectedPath = frameworkVersion >= localName.Version ? frameworkPath : localPath;
                if (selected.Location != expectedPath)
                {
                    throw new InvalidOperationException($"Incorrect framework selection: {selected.Location}, expected {expectedPath}");
                }
                Console.WriteLine($"Selected {selected.FullName} from {selected.Location}");
            }

            Assembly implementation = context.LoadFromAssemblyName(new AssemblyName("Microsoft.Diagnostics.DebugServices.Implementation"));
            Assembly immutable = context.LoadFromAssemblyName(new AssemblyName("System.Collections.Immutable"));
            Assembly metadata = context.LoadFromAssemblyName(new AssemblyName("System.Reflection.Metadata"));
            AssemblyName metadataImmutableName = metadata.GetReferencedAssemblies()
                .Single(name => name.Name == "System.Collections.Immutable");
            if (AssemblyLoadContext.GetLoadContext(metadata).LoadFromAssemblyName(metadataImmutableName) != immutable)
            {
                throw new InvalidOperationException("Framework metadata and SOS must share the immutable collection contract.");
            }
            Type managerType = implementation.GetType("Microsoft.Diagnostics.DebugServices.Implementation.ServiceManager", throwOnError: true);
            object manager = Activator.CreateInstance(managerType);
            managerType.GetMethod("LoadExtension").Invoke(manager, new object[] { args[2] });
            IEnumerable<Assembly> loaded = (IEnumerable<Assembly>)managerType.GetProperty("ExtensionsLoaded").GetValue(manager);
            Assembly extension = loaded.Single();
            AssemblyLoadContext extensionContext = AssemblyLoadContext.GetLoadContext(extension);
            Assembly contract = extension.GetType("TestExtension.TestCommand1", throwOnError: true).BaseType.Assembly;
            if (extensionContext == context || extensionContext == AssemblyLoadContext.Default ||
                AssemblyLoadContext.GetLoadContext(contract) != context ||
                extensionContext.LoadFromAssemblyName(immutable.GetName()) != immutable)
            {
                throw new InvalidOperationException("The optional extension did not share contracts with the SOS context.");
            }
            Console.WriteLine("Optional extension has its own ALC and shares SOS contracts.");
            if (Initialize(dependencyPath) != conflictVersion)
            {
                throw new InvalidOperationException("SOS changed the competing extension's dependency.");
            }
            return 0;
        }
    }
}
