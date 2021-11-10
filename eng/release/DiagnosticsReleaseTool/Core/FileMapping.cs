// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ReleaseTool.Core
{
    public struct FileMapping
    {
        public FileMapping(string localSourcePath, string relativeOutputPath)
        {
            LocalSourcePath = localSourcePath;
            RelativeOutputPath = relativeOutputPath;
        }

        public string LocalSourcePath { get; }

        public string RelativeOutputPath { get; }
    }
}