// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// Thrown when a fluent SOS assertion fails. The message is intentionally rich —
/// it names the host, the command, what was expected, and echoes the full captured
/// output. This is the single biggest day-to-day improvement over the legacy
/// <c>VERIFY:</c> model, whose failures only ever said
/// "Debugger output did not match the expression: ...".
/// </summary>
public sealed class SosAssertException : Exception
{
    public SosAssertException(string host, string command, string expectation, string actualOutput)
        : base(Build(host, command, expectation, actualOutput))
    {
    }

    private static string Build(string host, string command, string expectation, string actualOutput)
    {
        string body = string.IsNullOrEmpty(actualOutput) ? "(no output)" : actualOutput.TrimEnd();
        return $"""
            SOS assertion failed.
              host:     {host}
              command:  {command}
              expected: {expectation}
            --- actual output ---
            {body}
            ---------------------
            """;
    }
}
