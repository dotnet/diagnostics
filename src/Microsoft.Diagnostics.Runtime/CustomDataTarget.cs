// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// A user-defined data reader.
    /// Note that this class will be kept alive by <see cref="DataTarget"/> until <see cref="DataTarget.Dispose"/>
    /// is called.
    /// </summary>
    public class CustomDataTarget : IDisposable
    {
        /// <summary>
        /// The data reader that ClrMD will use to read data from the target.
        /// </summary>
        public IDataReader DataReader { get; set; }

        /// <summary>
        ///  An optional set of cache options.  Returning null from this property will use ClrMD's default
        ///  cache options.
        /// </summary>
        public CacheOptions? CacheOptions { get; set; }

        /// <summary>
        /// An optional file locator.  Returning null from this property will use ClrMD's file binary
        /// locator, which uses either <see cref="DefaultSymbolPath"/> (if non null) or the _NT_SYMBOL_PATH (if
        /// <see cref="DefaultSymbolPath"/> is null) environment variable to search for missing binaries.
        /// </summary>
        public IFileLocator? FileLocator { get; set; }

        /// <summary>
        /// If <see cref="FileLocator"/> is null, this path will be used as the symbol path for the default
        /// binary locator.  This property has no effect if <see cref="FileLocator"/> is non-null.
        /// </summary>
        public string? DefaultSymbolPath { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="reader">A non-null IDataReader.</param>
        public CustomDataTarget(IDataReader reader)
        {
            DataReader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        /// <summary>
        /// Dispose method.  Called when <see cref="DataTarget.Dispose"/> is called.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        ~CustomDataTarget() => Dispose(disposing: false);

        /// <summary>
        /// Dispose implementation.  The default implementation will call Dispose() on DataReader if
        /// it implements IDisposable.
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (DataReader is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        public override string ToString() => DataReader?.DisplayName ?? GetType().Name;
    }
}