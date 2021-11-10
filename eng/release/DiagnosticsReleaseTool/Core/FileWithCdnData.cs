// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ReleaseTool.Core
{
    public struct FileWithCdnData
    {
        public string Comment { get; }

        public string FilePath { get; }

        public string Sha512 { get; }

        public string PublishUrlSubPath { get; }

        public string AkaMsLink { get; }

        public FileWithCdnData(string filePath, string sha512, string publishUrlSubPath, string akaMsLink, string comment)
        {
            FilePath = filePath;
            Sha512 = sha512;
            PublishUrlSubPath = publishUrlSubPath;
            AkaMsLink = akaMsLink;
            Comment = comment;
        }
    }
}