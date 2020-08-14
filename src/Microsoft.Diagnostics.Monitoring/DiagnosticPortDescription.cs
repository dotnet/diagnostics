// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.Monitoring
{
    internal class DiagnosticPortDescription : IDiagnosticPortDescription
    {
        private DiagnosticPortDescription(DiagnosticPortConnectionMode mode, string name)
        {
            Mode = mode;
            Name = name;
        }

        public static IDiagnosticPortDescription Parse(string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                return new DiagnosticPortDescription(DiagnosticPortConnectionMode.Connect, null);
            }
            else
            {
                return new DiagnosticPortDescription(DiagnosticPortConnectionMode.Listen, description);
            }
        }

        public string Name { get; }

        public DiagnosticPortConnectionMode Mode { get; }
    }
}
