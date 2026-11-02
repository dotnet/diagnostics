// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

internal static class CDacOverride
{
    private static readonly object s_copyLock = new();
    private static readonly HashSet<string> s_destinations = new(StringComparer.OrdinalIgnoreCase);

    internal static string DacFileName =>
        OperatingSystem.IsWindows() ? "mscordaccore_universal.dll" :
        OperatingSystem.IsMacOS() ? "libmscordaccore_universal.dylib" : "libmscordaccore_universal.so";

    internal static string DbiFileName =>
        OperatingSystem.IsWindows() ? "mscordbi_universal.dll" :
        OperatingSystem.IsMacOS() ? "libmscordbi_universal.dylib" : "libmscordbi_universal.so";

    internal static void Prepare(IToolLayout layout, Host host)
    {
        string? overrideDirectory = layout.CDacOverrideDirectory;
        if (overrideDirectory is null)
        {
            return;
        }

        string destinationDirectory = host switch
        {
            Host.Cdb or Host.Lldb => layout.SosDirectory,
            Host.DotnetDump => Path.Combine(Path.GetDirectoryName(layout.DotNetDump.AssemblyPath
                ?? throw new InvalidOperationException("A cDAC override requires a managed dotnet-dump entry point."))!, RepoLayout.Rid),
            _ => throw new ArgumentException($"Unknown host '{host}'.", nameof(host)),
        };

        lock (s_copyLock)
        {
            if (s_destinations.Contains(destinationDirectory))
            {
                return;
            }

            CopyOverrideFile(DacFileName);
            CopyOverrideFile(DbiFileName);
            s_destinations.Add(destinationDirectory);

            void CopyOverrideFile(string fileName)
            {
                string source = Path.Combine(overrideDirectory, fileName);
                string destination = Path.Combine(destinationDirectory, fileName);
                if (!File.Exists(destination) || !FilesEqual(source, destination))
                {
                    File.Copy(source, destination, overwrite: true);
                }
            }
        }
    }

    private static bool FilesEqual(string leftPath, string rightPath)
    {
        FileInfo leftInfo = new(leftPath);
        FileInfo rightInfo = new(rightPath);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using FileStream left = File.OpenRead(leftPath);
        using FileStream right = File.OpenRead(rightPath);
        int leftByte;
        while ((leftByte = left.ReadByte()) != -1)
        {
            if (leftByte != right.ReadByte())
            {
                return false;
            }
        }
        return right.ReadByte() == -1;
    }
}
