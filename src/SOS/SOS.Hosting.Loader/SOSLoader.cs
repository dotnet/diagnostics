// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SOS.Hosting.Loader
{
    public static class SOSLoader
    {
        private static readonly Dictionary<string, Func<string, int>> s_initializers = new();

        public static int Initialize(string extensionPath)
        {
            if (Environment.Version.Major < 8)
            {
                throw new NotSupportedException("SOS requires .NET 8 or later.");
            }

            string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(extensionPath)), "net8.0");
            Func<string, int> initialize;
            lock (s_initializers)
            {
                if (!s_initializers.TryGetValue(directory, out initialize))
                {
                    SOSLoadContext context = new(directory);
                    Assembly assembly = context.LoadFromAssemblyPath(Path.Combine(directory, "SOS.Extensions.dll"));
                    MethodInfo method = assembly.GetType("SOS.Extensions.HostServices", throwOnError: true)
                        .GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) });
                    if (method is null)
                    {
                        throw new MissingMethodException("SOS.Extensions.HostServices", "Initialize");
                    }
                    initialize = method.CreateDelegate<Func<string, int>>();
                    s_initializers.Add(directory, initialize);
                }
            }
            return initialize(extensionPath);
        }
    }
}
