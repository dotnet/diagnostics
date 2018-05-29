using Debugger.Tests;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Debugger.Tests.Build
{
    public static partial class AssertX
    {
        public static void DirectoryExists(string dirDescriptiveName, string dirPath, ITestOutputHelper output)
        {
            if (!Directory.Exists(dirPath))
            {
                string errorMessage = "Expected " + dirDescriptiveName + " to exist: " + dirPath;
                output.WriteLine(errorMessage);
                try
                {
                    string parentDir = dirPath;
                    while (true)
                    {
                        if (Directory.Exists(Path.GetDirectoryName(parentDir)))
                        {
                            output.WriteLine("First parent directory that exists: " + Path.GetDirectoryName(parentDir));
                            break;
                        }
                        if (Path.GetDirectoryName(parentDir) == parentDir)
                        {
                            output.WriteLine("Also unable to find any parent directory that exists");
                            break;
                        }
                        parentDir = Path.GetDirectoryName(parentDir);
                    }
                }
                catch (Exception e)
                {
                    output.WriteLine("Additional error while trying to diagnose missing directory:");
                    output.WriteLine(e.GetType() + ": " + e.Message);
                }
                throw new DirectoryNotFoundException(errorMessage);
            }
        }

        public static void FileExists(string fileDescriptiveName, string filePath, ITestOutputHelper output)
        {
            if (!File.Exists(filePath))
            {
                string errorMessage = "Expected " + fileDescriptiveName + " to exist: " + filePath;
                output.WriteLine(errorMessage);
                try
                {
                    string parentDir = filePath;
                    while (true)
                    {
                        if (Directory.Exists(Path.GetDirectoryName(parentDir)))
                        {
                            output.WriteLine("First parent directory that exists: " + Path.GetDirectoryName(parentDir));
                            break;
                        }
                        if (Path.GetDirectoryName(parentDir) == parentDir)
                        {
                            output.WriteLine("Also unable to find any parent directory that exists");
                            break;
                        }
                        parentDir = Path.GetDirectoryName(parentDir);
                    }
                }
                catch (Exception e)
                {
                    output.WriteLine("Additional error while trying to diagnose missing file:");
                    output.WriteLine(e.GetType() + ": " + e.Message);
                }
                throw new FileNotFoundException(errorMessage);
            }
        }
    }
}


