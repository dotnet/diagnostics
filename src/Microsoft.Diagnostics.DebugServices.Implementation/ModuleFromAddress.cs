// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.FileFormats.PE;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Create a IModule instance from a base address.
    /// </summary>
    public class ModuleFromAddress : Module
    {
        private Version _version;
        private string _versionString;

        public ModuleFromAddress(ModuleService moduleService, int moduleIndex, ulong imageBase, ulong imageSize, string imageName)
            : base(moduleService.Services)
        {
            ModuleService = moduleService;
            ModuleIndex = moduleIndex;
            ImageBase = imageBase;
            ImageSize = imageSize;
            FileName = imageName;
        }

        #region IModule

        public override uint? IndexTimeStamp
        {
            get {
                PEFile peFile = Services.GetService<PEFile>();
                return peFile?.Timestamp;
            }
        }

        public override uint? IndexFileSize
        {
            get {
                PEFile peFile = Services.GetService<PEFile>();
                return peFile?.SizeOfImage;
            }
        }

        public override Version GetVersionData()
        {
            if (InitializeValue(Module.Flags.InitializeVersion))
            {
                _version = GetVersionInner();
            }
            return _version;
        }

        public override string GetVersionString()
        {
            if (InitializeValue(Module.Flags.InitializeProductVersion))
            {
                _versionString = GetVersionStringInner();
            }
            return _versionString;
        }

        public override string LoadSymbols()
        {
            return ModuleService.SymbolService.DownloadSymbolFile(this);
        }

        #endregion

        protected override ModuleService ModuleService { get; }
    }
}
