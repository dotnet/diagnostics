// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class ModuleImageInfoTests
    {
        [Fact]
        public void OfflineDumpHostDoesNotChangeDefaultSymbolSources()
        {
            using TestDump offline = new(new TestConfiguration(), enableSymbolServer: false);
            using TestDump online = new(new TestConfiguration());
            SymbolService offlineSymbols = Assert.IsType<SymbolService>(offline.ServiceContainer.GetService<ISymbolService>());
            SymbolService onlineSymbols = Assert.IsType<SymbolService>(online.ServiceContainer.GetService<ISymbolService>());

            Assert.Equal(string.Empty, offlineSymbols.FormatSymbolStores());
            Assert.Contains($"Server: {SymbolService.MsdlSymbolServer}", onlineSymbols.FormatSymbolStores());
            Assert.Contains("Cache:", onlineSymbols.FormatSymbolStores());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AvailablePEImagePreservesClassification(bool managed)
        {
            using ImageTarget target = new(CreatePEImage(managed), OSPlatform.Windows);
            IModule module = target.GetModule();

            Assert.True(module.Services.GetService<IModuleImageInfo>().IsImageInfoAvailable);
            Assert.Equal(managed, module.IsManaged);
            Assert.True(module.IsPEImage);
            Assert.False(module.IsFileLayout);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(false, true)]
        public void MissingOrTruncatedImageIsUnavailable(bool windows, bool truncated)
        {
            byte[] image = truncated ? new byte[] { 0x4d, 0x5a } : Array.Empty<byte>();
            using ImageTarget target = new(image, windows ? OSPlatform.Windows : OSPlatform.Linux);
            IModule module = target.GetModule();

            Assert.False(module.Services.GetService<IModuleImageInfo>().IsImageInfoAvailable);
            Assert.False(module.IsManaged);
            Assert.Empty(module.GetPdbFileInfos());
            Assert.False(module.Services.GetService<IModuleImageInfo>().IsImageInfoAvailable);
        }

        [Fact]
        public void NonPEImageIsKnownNative()
        {
            using ImageTarget target = new(new byte[] { 0x7f, 0x45, 0x4c, 0x46 }, OSPlatform.Linux);
            IModule module = target.GetModule();

            Assert.True(module.Services.GetService<IModuleImageInfo>().IsImageInfoAvailable);
            Assert.False(module.IsPEImage);
            Assert.False(module.IsManaged);
        }

        [Fact]
        public void DebuggerNativeModuleDoesNotProbeMemory()
        {
            using ImageTarget target = new(Array.Empty<byte>(), OSPlatform.OSX, probeSupported: false, hostType: HostType.Lldb);
            IModule module = target.GetModule();

            Assert.True(module.Services.GetService<IModuleImageInfo>().IsImageInfoAvailable);
            Assert.False(module.IsManaged);
            Assert.False(module.IsPEImage);
            Assert.Empty(module.GetPdbFileInfos());
            Assert.Equal(0, target.ReadCount);
        }

        [Fact]
        public void LldbManagedModuleStillProbesImage()
        {
            using ImageTarget target = new(CreatePEImage(managed: true), OSPlatform.OSX, hostType: HostType.Lldb);
            IModule module = target.GetModule();

            Assert.True(module.Services.GetService<IModuleImageInfo>().IsImageInfoAvailable);
            Assert.True(module.IsManaged);
            Assert.True(module.IsPEImage);
            Assert.True(target.ReadCount > 0);
        }

        private static byte[] CreatePEImage(bool managed)
        {
            byte[] file = File.ReadAllBytes(typeof(IModule).Assembly.Location);
            using PEReader reader = new(new MemoryStream(file));
            PEHeader header = reader.PEHeaders.PEHeader;
            byte[] image = new byte[header.SizeOfImage];
            file.AsSpan(0, header.SizeOfHeaders).CopyTo(image);
            foreach (SectionHeader section in reader.PEHeaders.SectionHeaders)
            {
                file.AsSpan(section.PointerToRawData, section.SizeOfRawData).CopyTo(image.AsSpan(section.VirtualAddress));
            }
            if (!managed)
            {
                // Remove the CLR directory to exercise native classification with an otherwise valid PE.
                int peOffset = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3c));
                int directoriesOffset = peOffset + 24 + (header.Magic == PEMagic.PE32 ? 96 : 112);
                image.AsSpan(directoriesOffset + 14 * 8, 8).Clear();
            }
            return image;
        }

        private sealed class ImageTarget : Target, IMemoryService, IDisposable
        {
            internal const ulong ModuleAddress = 0x10000;
            private readonly byte[] _image;

            public ImageTarget(byte[] image, OSPlatform platform, bool probeSupported = true, HostType hostType = HostType.DotnetDump)
                : base(CreateHost(hostType), dumpPath: null)
            {
                _image = image;
                OperatingSystem = platform;
                Architecture = Architecture.X64;
                _serviceContainerFactory.AddServiceFactory<IMemoryService>((_) => this);
                _serviceContainerFactory.AddServiceFactory<IModuleService>((services) => new ImageModuleService(services, (ulong)Math.Max(image.Length, 4096), probeSupported));
                Finished();
            }

            public int ReadCount { get; private set; }

            public int PointerSize => 8;

            public IModule GetModule() => Services.GetService<IModuleService>().GetModuleFromBaseAddress(ModuleAddress);

            public bool ReadMemory(ulong address, Span<byte> buffer, out int bytesRead)
            {
                ReadCount++;
                bytesRead = 0;
                if (address >= ModuleAddress && address - ModuleAddress < (ulong)_image.Length)
                {
                    int offset = (int)(address - ModuleAddress);
                    bytesRead = Math.Min(buffer.Length, _image.Length - offset);
                    _image.AsSpan(offset, bytesRead).CopyTo(buffer);
                }
                return bytesRead > 0;
            }

            public bool WriteMemory(ulong address, Span<byte> buffer, out int bytesWritten) => throw new NotSupportedException();

            public void Dispose()
            {
                // The target owns this memory service, so remove it before destroying its services.
                FlushService<IMemoryService>();
                Destroy();
            }

            private static Host CreateHost(HostType hostType)
            {
                Host host = new(hostType);
                host.CreateServiceContainer();
                return host;
            }
        }

        private sealed class ImageModuleService(IServiceProvider services, ulong imageSize, bool probeSupported) : ModuleService(services)
        {
            protected override Dictionary<ulong, IModule> GetModulesInner() => new()
            {
                [ImageTarget.ModuleAddress] = new ImageModule(this, Services, imageSize, probeSupported)
            };
        }

        private sealed class ImageModule : Module
        {
            public ImageModule(ModuleService moduleService, IServiceProvider services, ulong imageSize, bool probeSupported)
                : base(services, probeSupported)
            {
                ModuleService = moduleService;
                ImageBase = ImageTarget.ModuleAddress;
                ImageSize = imageSize;
                FileName = "test.dll";
            }

            protected override ModuleService ModuleService { get; }

            public override Version GetVersionData() => throw new NotSupportedException();

            public override string GetVersionString() => throw new NotSupportedException();

            public override string LoadSymbols() => throw new NotSupportedException();
        }
    }
}
