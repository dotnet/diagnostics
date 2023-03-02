// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;

namespace ReleaseTool.Core
{
    public struct SingleFileResult : IEnumerable<(FileMapping fileMap, FileMetadata fileMetadata)>
    {
        private readonly (FileMapping fileMap, FileMetadata fileMetadata) _fileData;

        public IEnumerator<(FileMapping fileMap, FileMetadata fileMetadata)> GetEnumerator() => GetInnerEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetInnerEnumerator();

        public SingleFileResult(FileMapping fileMap, FileMetadata fileMetadata)
        {
            _fileData = (fileMap, fileMetadata);
        }

        private Enumerator GetInnerEnumerator()
        {
            return new Enumerator(this);
        }

        public struct Enumerator : IEnumerator<(FileMapping fileMap, FileMetadata fileMetadata)>
        {
            private bool _consumed;

            public Enumerator(SingleFileResult singleFileResults) : this()
            {
                Current = singleFileResults._fileData;
            }

            public (FileMapping fileMap, FileMetadata fileMetadata) Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                bool cur = _consumed;
                _consumed = true;
                return !cur;
            }

            public void Reset()
            {
                _consumed = false;
            }
        }
    }
}
