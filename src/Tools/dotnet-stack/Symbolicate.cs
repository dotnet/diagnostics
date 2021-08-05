// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Tools.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.IO;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Diagnostics.Tools.Stack
{
    internal static class SymbolicateHandler
    {
        private static readonly Regex s_regex = new Regex(@" at (?<type>[\w+\.?]+)\.(?<method>\w+)\((?<params>.*)\) in (?<filename>[\w+\.?]+)(\.dll|\.ni\.dll): token (?<token>0x\d+)\+(?<offset>0x\d+)", RegexOptions.Compiled);
        private static readonly Regex s_verifyRegex = new Regex(@"at (?<typeMethod>.*)\((?<params>.*?)\) in (?<filename>.*)token(?<token>.*)\+(?<offset>.*)", RegexOptions.Compiled);

        delegate void SymbolicateDelegate(IConsole console, FileInfo inputPath, DirectoryInfo[] searchDir, FileInfo output, bool stdout);

        /// <summary>
        /// Get the line number from the Method Token and IL Offset at the stacktrace
        /// </summary>
        /// <param name="console"></param>
        /// <param name="inputPath">The input path for file with stacktrace text</param>
        /// <param name="searchDir">All paths in the directory to the assembly and pdb where the exception occurred</param>
        /// <param name="output">The output path for the extracted line number data</param>
        /// <returns></returns>
        private static void Symbolicate(IConsole console, FileInfo inputPath, DirectoryInfo[] searchDir, FileInfo output, bool stdout)
        {
            try
            {
                List<string> searchPaths = new List<string>();
                foreach (var path in searchDir)
                {
                    searchPaths.Add(path.FullName);
                }
                searchPaths.Add(Directory.GetCurrentDirectory());

                if (output == null)
                {
                    output = new FileInfo(inputPath.FullName + ".symbolicated");
                }

                Symbolicator(console, searchPaths, inputPath.FullName, output.FullName, stdout);
            }
            catch (Exception e)
            {
                console.Error.WriteLine(e.Message);
            }
        }

        private static void Symbolicator(IConsole console, List<string> searchPaths, string inputPath, string outputPath, bool isStdout)
        {
            CreateSymbolicateFile(console, GetSearchPathList(searchPaths), inputPath, outputPath, isStdout);
        }

        private static List<string> GetSearchPathList(List<string> searchPaths)
        {
            List<string> peFiles = GrabFiles(searchPaths, "*.dll");
            if (peFiles.Count == 0)
            {
                throw new FileNotFoundException("Assembly file not found\n");
            }
            peFiles.Sort();

            List<string> pdbFiles = GrabFiles(searchPaths, "*.pdb");
            if (pdbFiles.Count == 0)
            {
                throw new FileNotFoundException("PDB file not found\n");
            }
            pdbFiles.Sort();

            List<string> searchList = new List<string>();
            int pdbCnt = 0;
            for (int peCnt = 0; peCnt < peFiles.Count; peCnt++)
            {
                int compare = string.Compare(Path.GetFileNameWithoutExtension(peFiles[peCnt]), Path.GetFileNameWithoutExtension(pdbFiles[pdbCnt]), StringComparison.OrdinalIgnoreCase);
                if (compare == 0)
                {
                    searchList.Add(Path.GetFileName(peFiles[peCnt]));
                }
                else if (compare > 0)
                {
                    pdbCnt++;
                    peCnt--;
                }
                if (pdbCnt == pdbFiles.Count) break;
            }
            return searchList;
        }

        private static List<string> GrabFiles(List<string> paths, string searchPattern)
        {
            List<string> files = new List<string>();
            foreach (var assemDir in paths)
            {
                if (Directory.Exists(assemDir))
                {
                    foreach (var peFile in Directory.GetFiles(assemDir, searchPattern, SearchOption.AllDirectories))
                    {
                        files.Add(peFile);
                    }
                }
            }
            return files;
        }

        private static void CreateSymbolicateFile(IConsole console, List<string> searchPathList, string inputPath, string outputPath, bool isStdout)
        {
            try
            {
                string ret = string.Empty;
                using StreamWriter fileStreamWriter = new StreamWriter(new FileStream(outputPath, FileMode.Create, FileAccess.Write));
                using StreamReader fileStreamReader = new StreamReader(new FileStream(inputPath, FileMode.Open, FileAccess.Read));
                while (!fileStreamReader.EndOfStream)
                {
                    string line = fileStreamReader.ReadLine();
                    if (!s_regex.Match(line).Success)
                    {
                        ret = GetVerifiedStackTrace(line);
                        if (ret == line)
                        {
                            fileStreamWriter?.WriteLine(ret);
                            if (isStdout) console.Out.WriteLine(ret);
                            continue;
                        }
                        line = ret;
                    }
                    ret = GetRegex(line, searchPathList);
                    fileStreamWriter?.WriteLine(ret);
                    if (isStdout) console.Out.WriteLine(ret);
                }
                console.Out.WriteLine($"\nOutput: {outputPath}\n");
            }
            catch (Exception e)
            {
                console.Error.WriteLine(e.Message);
            }
        }

        private static string GetVerifiedStackTrace(string line)
        {
            Match match = s_verifyRegex.Match(line);
            StringBuilder str = new StringBuilder();
            str.Append("   at ");
            str.Append(match.Groups["typeMethod"].Value.TrimEnd());
            str.Append("(");
            str.Append(match.Groups["params"].Value);
            str.Append(") in ");
            str.Append(match.Groups["filename"].Value.Replace(":", "").Trim());
            str.Append(": token ");
            str.Append(match.Groups["token"].Value.Trim());
            str.Append("+");
            str.Append(match.Groups["offset"].Value.TrimStart().Split(" ")[0]);

            if (s_regex.Match(str.ToString()).Success)
            {
                return str.ToString();
            }
            return line;
        }

        internal sealed class StackTraceInfo
        {
            public string Type;
            public string Method;
            public string Param;
            public string Assembly;
            public string Pdb;
            public string Token;
            public string Offset;
        }

        private static string GetRegex(string line, List<string> searchPathList)
        {
            string ret = line;
            Match match = s_regex.Match(line);
            if (!match.Success)
            {
                return line;
            }

            StackTraceInfo stInfo = new StackTraceInfo()
            {
                Type = match.Groups["type"].Value,
                Method = match.Groups["method"].Value,
                Param = match.Groups["params"].Value,
                Assembly = match.Groups["filename"].Value,
                Token = match.Groups["token"].Value,
                Offset = match.Groups["offset"].Value
            };
            stInfo.Pdb = stInfo.Assembly.Contains(".ni.dll") ? stInfo.Assembly.Replace(".ni.dll", ".pdb") : stInfo.Assembly.Replace(".dll", ".pdb");

            foreach (var path in searchPathList)
            {
                if (path.Contains(stInfo.Assembly))
                {
                    return GetLineFromMetadata(GetMetadataReader(path), ret, stInfo);
                }
            }
            return ret;
        }

        private static MetadataReader GetMetadataReader(string filePath)
        {
            try
            {
                Func<string, Stream> streamProvider = sp => new FileStream(sp, FileMode.Open, FileAccess.Read);
                using Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                if (stream != null)
                {
                    MetadataReaderProvider provider = null;
                    if (filePath.Contains(".dll"))
                    {
                        using PEReader peReader = new PEReader(stream);
                        if (!peReader.TryOpenAssociatedPortablePdb(filePath, streamProvider, out provider, out string pdbPath))
                        {
                            return null;
                        }
                    }
                    /*else if (filePath.Contains(".pdb"))
                    {
                        provider = MetadataReaderProvider.FromPortablePdbStream(stream);
                    }*/
                    return provider.GetMetadataReader();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetLineFromMetadata(MetadataReader reader, string line, StackTraceInfo stInfo)
        {
            if (reader != null)
            {
                Handle handle = MetadataTokens.Handle(Convert.ToInt32(stInfo.Token, 16));
                if (handle.Kind == HandleKind.MethodDefinition)
                {
                    MethodDebugInformationHandle methodDebugHandle = ((MethodDefinitionHandle)handle).ToDebugInformationHandle();
                    MethodDebugInformation methodInfo = reader.GetMethodDebugInformation(methodDebugHandle);
                    if (!methodInfo.SequencePointsBlob.IsNil)
                    {
                        SequencePointCollection sequencePoints = methodInfo.GetSequencePoints();
                        SequencePoint? bestPointSoFar = null;
                        foreach (SequencePoint point in sequencePoints)
                        {
                            if (point.Offset > Convert.ToInt64(stInfo.Offset, 16))
                                break;

                            if (point.StartLine != SequencePoint.HiddenLine)
                                bestPointSoFar = point;
                        }

                        if (bestPointSoFar.HasValue)
                        {
                            string sourceFile = reader.GetString(reader.GetDocument(bestPointSoFar.Value.Document).Name);
                            int sourceLine = bestPointSoFar.Value.StartLine;
                            return $"   at {stInfo.Type}.{stInfo.Method}({stInfo.Param}) in {sourceFile}:line {sourceLine}";
                        }
                    }
                }
            }
            return line;
        }

        public static Command SymbolicateCommand() =>
            new Command(
                name: "symbolicate", description: "Get the line number from the Method Token and IL Offset in a stacktrace")
            {
                // Handler
                HandlerDescriptor.FromDelegate((SymbolicateDelegate)Symbolicate).GetCommandHandler(),
                // Arguments and Options
                InputFileArgument(),
                SearchDirectoryOption(),
                OutputFileOption(),
                StandardOutOption()
            };

        public static Argument<FileInfo> InputFileArgument() =>
            new Argument<FileInfo>(name: "input-path")
            {
                Description = "Path to the stacktrace text file",
                Arity = ArgumentArity.ExactlyOne
            }.ExistingOnly();

        public static Option<DirectoryInfo[]> SearchDirectoryOption() =>
            new Option<DirectoryInfo[]>(new[] { "-d", "--search-dir" }, "Path of multiple directories with assembly and pdb")
            {
                Argument = new Argument<DirectoryInfo[]>(name: "directory1 directory2 ...", getDefaultValue: () => new DirectoryInfo(Directory.GetCurrentDirectory()).GetDirectories())
                {
                    Arity = ArgumentArity.ZeroOrMore
                }.ExistingOnly()
            };

        public static Option<FileInfo> OutputFileOption() =>
            new Option<FileInfo>(new[] { "-o", "--output" }, "Output directly to a file (Default: <input-path>.symbolicated)")
            {
                Argument = new Argument<FileInfo>(name: "output-path")
                {
                    Arity = ArgumentArity.ZeroOrOne
                }
            };

        public static Option<bool> StandardOutOption() =>
            new Option<bool>(new[] { "-c", "--stdout" }, getDefaultValue: () => false, "Output directly to a console");
    }
}