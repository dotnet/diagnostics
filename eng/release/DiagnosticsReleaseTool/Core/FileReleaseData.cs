// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ReleaseTool.Core
{
    public class FileReleaseData
    {
        public FileReleaseData(FileMapping fileMap, FileMetadata fileMetadata, bool isAssetForPublicRelease = true)
            : this(fileMap, fileMetadata, isAssetForPublicRelease, null) { }

        private FileReleaseData(FileMapping fileMap, FileMetadata fileMetadata, bool isAssetForPublicRelease, string publishUri)
        {
            FileMap = fileMap;
            FileMetadata = fileMetadata;
            IsAssetForPublicRelease = isAssetForPublicRelease;
            PublishUri = publishUri;
        }

        public FileMapping FileMap { get; }
        public FileMetadata FileMetadata { get; }
        public bool IsAssetForPublicRelease { get; }
        public string PublishUri { get; internal set; }
    }
}
