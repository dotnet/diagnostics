// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.SymbolStore.SymbolStores;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    /// <summary>
    /// Test the service event implementation
    /// </summary>
    public class SymbolServiceTests : IHost
    {
        public SymbolServiceTests()
        {
        }

        [Fact]
        public void SymbolPathTests()
        {
            var symbolService = new SymbolService(this);
            Assert.False(symbolService.ParseSymbolPath("srv"));
            Assert.False(symbolService.ParseSymbolPath("cache"));
            Assert.False(symbolService.ParseSymbolPath("symsrv"));

            string defaultServer = $"Server: {SymbolService.MsdlSymbolServer}";
            string defaultPath = $"Cache: {symbolService.DefaultSymbolCache} {defaultServer}";
            string localSymbolCache = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "c:\\localsymbolcache" : "/home/foo/localsymbolcache";

            Assert.True(symbolService.ParseSymbolPath("srv*"));
            Assert.Equal(defaultServer, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath("srv**"));
            Assert.Equal(defaultPath, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath("symsrv*symsrv.dll*"));
            Assert.Equal(defaultServer, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath("cache*;srv*"));
            Assert.Equal(defaultPath, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();
            
            Assert.True(symbolService.ParseSymbolPath("srv*https://msdl.microsoft.com/download/symbols/"));
            Assert.Equal(defaultServer, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath($"srv**{SymbolService.MsdlSymbolServer}"));
            Assert.Equal(defaultPath, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath($"srv*{localSymbolCache}*{SymbolService.SymwebSymbolServer}"));
            string testpath1 = $"Cache: {localSymbolCache} Server: {SymbolService.SymwebSymbolServer}";
            Assert.Equal(testpath1, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath($"cache*{localSymbolCache};srv*"));
            string testpath2 = $"Cache: {localSymbolCache} Server: {SymbolService.MsdlSymbolServer}";
            Assert.Equal(testpath2, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath($"srv**{localSymbolCache}*http://msdl.microsoft.com/download/symbols/"));
            Assert.Equal($"Cache: {symbolService.DefaultSymbolCache} Cache: {localSymbolCache} Server: http://msdl.microsoft.com/download/symbols/", symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.True(symbolService.ParseSymbolPath($"symsrv*symsrv.dll*{localSymbolCache}*\\\\server\\share"));
                Assert.Equal($"Cache: {localSymbolCache} Cache: \\\\server\\share", symbolService.FormatSymbolStores());
                symbolService.DisableSymbolStore();

                Assert.True(symbolService.ParseSymbolPath("symsrv*symsrv.dll*d:\\data\\SYM\\symcache*\\\\aw0eus0symcache.file.core.windows.net\\Symbols*http://localhost/remote200/30e07e1454924e55901d7f693f7eddf1/0/x64/4542784547/remote"));
                Assert.Equal("Cache: d:\\data\\SYM\\symcache Cache: \\\\aw0eus0symcache.file.core.windows.net\\Symbols Server: http://localhost/remote200/30e07e1454924e55901d7f693f7eddf1/0/x64/4542784547/remote/", symbolService.FormatSymbolStores());
                symbolService.DisableSymbolStore();
            }

            string symbolDirectory = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "c:\\symbols\\" : "/home/foo/symbols/";
            Assert.True(symbolService.ParseSymbolPath(symbolDirectory));
            Assert.Equal($"Directory: {symbolDirectory}", symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            string symbolDirectory2 = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "c:\\foo\\bar" : "/home/foo/bar";
            Assert.True(symbolService.ParseSymbolPath($"{symbolDirectory};{symbolDirectory2};srv*"));
            Assert.Equal($"Directory: {symbolDirectory} Directory: {symbolDirectory2} " + defaultServer, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();
        }

        #region IHost

        public IServiceEvent OnShutdownEvent { get; } = new ServiceEvent();

        public IServiceEvent<ITarget> OnTargetCreate { get; } = new ServiceEvent<ITarget>();

        public HostType HostType => HostType.DotnetDump;

        public IServiceProvider Services => throw new NotImplementedException();

        public IEnumerable<ITarget> EnumerateTargets() => throw new NotImplementedException();

        #endregion
    }

    public static class SymbolServiceExtensions
    {
        public static string FormatSymbolStores(this SymbolService symbolService)
        {
            StringBuilder sb = new StringBuilder();
            symbolService.ForEachSymbolStore<Microsoft.SymbolStore.SymbolStores.SymbolStore>((symbolStore) => sb.AppendLine(symbolStore.ToString()));
            return sb.ToString().Replace(Environment.NewLine, " ").TrimEnd();
        }
    }
}
