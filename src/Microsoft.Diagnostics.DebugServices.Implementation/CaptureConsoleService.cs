// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public sealed class CaptureConsoleService : IConsoleService
    {
        private readonly CharToLineConverter _charToLineConverter;
        private readonly List<string> _builder = new();

        public CaptureConsoleService() => _charToLineConverter = new((line) => _builder.Add(line));

        public void Clear() => _builder.Clear();

        public IReadOnlyList<string> OutputLines => _builder;

        public override string ToString() => string.Concat(_builder);

        #region IConsoleService

        bool IConsoleService.SupportsDml => false;

        int IConsoleService.WindowWidth => int.MaxValue;

        CancellationToken IConsoleService.CancellationToken { get; set; } = CancellationToken.None;

        void IConsoleService.WriteString(OutputType type, OutputLevel level, string text)
        {
            switch (type)
            {
                case OutputType.Default:
                    _charToLineConverter.Input(text);
                    break;
                case OutputType.Logging:
                    break;
                default:
                    throw new NotSupportedException($"Output type {type} is not supported in the capture console");
            }
        }

        #endregion
    }
}
