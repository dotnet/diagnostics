using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    /// <summary>
    /// We want to make sure we destroy files we finish streaming.
    /// We want to make sure that we stream out files since we compress on the fly; the size cannot be known upfront.
    /// CONSIDER The above implies knowledge of how the file is used by the rest api.
    /// </summary>
    internal sealed class FileStreamWrapper : FileStream
    {
        public FileStreamWrapper(string path) : base(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096, FileOptions.DeleteOnClose)
        {
        }

        public override bool CanSeek => false;
    }
}
