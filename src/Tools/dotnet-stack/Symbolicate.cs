// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DiaSymReader.Tools;
using Microsoft.Tools.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.IO;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Microsoft.Diagnostics.Tools.Stack
{
    internal static class SymbolicateHandler
    {
        // Temporary folder to store the files converted from pdb to xml
        private static readonly string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        delegate void SymbolicateDelegate(IConsole console, FileInfo inputPath, DirectoryInfo[] searchDir, string output);

        /// <summary>
        /// Get the line number from the Method Token and IL Offset at the stacktrace
        /// </summary>
        /// <param name="console"></param>
        /// <param name="inputPath">The input path for file with stacktrace text</param>
        /// <param name="searchDir">All paths in the directory to the assembly and pdb where the exception occurred</param>
        /// <param name="output">The output path for the extracted line number data</param>
        /// <returns></returns>
        private static void Symbolicate(IConsole console, FileInfo inputPath, DirectoryInfo[] searchDir, string output)
        {
            try
            {
                List<string> search_paths = new List<string>();
                if (searchDir.Length == 0)
                {
                    search_paths.Add(Directory.GetCurrentDirectory());
                }
                else
                {
                    foreach (var path in searchDir)
                    {
                        search_paths.Add(path.FullName);
                    }
                }

                Symbolicator(console, PdbToXmlConvert(search_paths), inputPath.FullName, output);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
            }
        }

        private static List<string> PdbToXmlConvert(List<string> searchPaths)
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

            Directory.CreateDirectory(tempDirectory);

            List<string> xmlList = new List<string>();
            foreach (var peFile in peFiles)
            {
                if (pdbFiles.Count == 0) break;
                foreach (var pdbFile in pdbFiles)
                {
                    if (Path.GetFileNameWithoutExtension(peFile) == Path.GetFileNameWithoutExtension(pdbFile))
                    {
                        string xmlPath = Path.Combine(tempDirectory, Path.GetFileName(Path.ChangeExtension(peFile, "xml")));
                        GenXmlFromPdb(peFile, pdbFile, xmlPath);
                        xmlList.Add(xmlPath);
                        pdbFiles.Remove(pdbFile);
                        break;
                    }
                }
            }
            return xmlList;
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

        private static void GenXmlFromPdb(string assemblyPath, string pdbPath, string xmlPath)
        {
            using var peStream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read);
            using var pdbStream = new FileStream(pdbPath, FileMode.Open, FileAccess.Read);
            using var dstFileStream = new FileStream(xmlPath, FileMode.Create, FileAccess.ReadWrite);
            using var sw = new StreamWriter(dstFileStream, Encoding.UTF8);
            PdbToXmlOptions options = PdbToXmlOptions.ResolveTokens | PdbToXmlOptions.IncludeTokens;

            PdbToXmlConverter.ToXml(sw, pdbStream, peStream, options);
        }

        private static void RemoveTempDirectory()
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        private static void Symbolicator(IConsole console, List<string> xmlList, string inputPath, string outputPath)
        {
            if (xmlList.Count == 0)
            {
                RemoveTempDirectory();
                throw new FileNotFoundException("Xml file not found\n");
            }

            GetLineFromStack(console, xmlList, inputPath, outputPath);

            RemoveTempDirectory();
        }

        internal sealed class StackTraceInfo
        {
            public string Type;
            public string Method;
            public string Param;
            public string Assembly;
            public string Token;
            public string Offset;
            public string Document;
            public string Filepath;
            public string Filename;
            public string StartLine;
            public string EndLine;
        }

        private static void GetLineFromStack(IConsole console, List<string> xmlList, string inputPath, string outputPath)
        {
            try
            {
                List<string> outputString = new List<string>();
                using StreamReader fsr = new StreamReader(new FileStream(inputPath, FileMode.Open, FileAccess.Read));
                while (!fsr.EndOfStream)
                {
                    string line = fsr.ReadLine();
                    if (!line.Contains("at ") || !line.Contains("+"))
                    {
                        outputString.Add(line);
                        console.Out.WriteLine($"{line}");
                        continue;
                    }
                    string ret = GetRegex(line, xmlList);
                    outputString.Add(ret);
                    console.Out.WriteLine($"{ret}");
                }

                string output = string.Empty;
                if (outputPath != null)
                {
                    using StreamWriter fsw = new StreamWriter(new FileStream(outputPath, FileMode.Create, FileAccess.Write));
                    foreach (var str in outputString)
                    {
                        fsw.WriteLine(str);
                    }
                    output = $"\nOutput: {outputPath}\n";
                }
                console.Out.WriteLine($"{output}");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
            }
        }

        private static string GetRegex(string line, List<string> xmlList)
        {
            string ret = line;
            string logtagStr = Regex.Match(line, "(?<splitdata>.*?) at").Groups["splitdata"].Value;
            if (logtagStr.Length != 0)
            {
                ret = line.Replace(logtagStr, "");
            }
            string typeMethodStr = Regex.Match(line, "at (?<splitdata>.*?)\\((.*)\\)").Groups["splitdata"].Value;
            string methodStr = typeMethodStr.Split(".")[typeMethodStr.Split(".").Length - 1];
            string typenameStr = typeMethodStr.Replace("." + methodStr, "");
            string parameterStr = Regex.Match(line, methodStr + "\\((?<splitdata>.*?)\\)").Groups["splitdata"].Value;
            string assemblyStr = Regex.Match(line, " in (?<splitdata>.*?)\\: ").Groups["splitdata"].Value;
            string[] tokenOffsetStr = Regex.Match(line, "\\: token (?<splitdata>.*)?").Groups["splitdata"].Value.Split("+");
            string xmlStr = assemblyStr.Contains(".ni.dll") ? assemblyStr.Replace(".ni.dll", ".xml") : assemblyStr.Replace(".dll", ".xml");

            if (tokenOffsetStr.Length != 2 || methodStr == "" || typenameStr == "" || assemblyStr == "")
            {
                return ret;
            }

            StackTraceInfo stInfo = new StackTraceInfo() { Type = typenameStr, Method = methodStr, Assembly = assemblyStr, Token = tokenOffsetStr[0], Offset = tokenOffsetStr[1] };

            foreach (var xmlPath in xmlList)
            {
                if (xmlPath.Contains(xmlStr))
                {
                    GetLineFromXml(xmlPath, stInfo);
                    if (stInfo.Filepath != null && stInfo.StartLine != null)
                    {
                        ret = $"   at {stInfo.Type}.{stInfo.Method}({parameterStr}) in {stInfo.Filepath}:line {stInfo.StartLine}";
                        break;
                    }
                }
            }
            return ret;
        }

        private static void GetLineFromXml(string xmlPath, StackTraceInfo stInfo)
        {
            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(xmlPath);
                XmlElement xRoot = xmlDoc.DocumentElement;
                XmlNodeList xnList = xRoot.ChildNodes;
                int xnCount = xnList.Count;
                if (xnCount > 0)
                {
                    for (int i = xnCount - 1; i >= 0; i--)
                    {
                        XmlNode node = xnList[i];
                        if (node.Name == "files")
                        {
                            ParseFile(node.ChildNodes, stInfo);
                        }
                        else if (node.Name == "methods")
                        {
                            ParseMethod(node.ChildNodes, stInfo);
                        }
                    }
                }
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e);
            }
        }

        private static void ParseFile(XmlNodeList xn, StackTraceInfo stInfo)
        {
            try
            {
                foreach (XmlNode node in xn)
                {
                    if (stInfo.Document == node.Attributes["id"].Value)
                    {
                        stInfo.Filepath = node.Attributes["name"].Value;
                        stInfo.Filename = Path.GetFileName(node.Attributes["name"].Value);
                    }
                }
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e);
            }
        }

        private static void ParseMethod(XmlNodeList xn, StackTraceInfo stInfo)
        {
            try
            {
                foreach (XmlNode node in xn)
                {
                    if (stInfo.Type == node.Attributes["containingType"].Value &&
                        stInfo.Method == node.Attributes["name"].Value &&
                        stInfo.Token == node.Attributes["token"].Value)
                    {
                        if (node.Attributes.Item(2).Name == "parameterNames")
                        {
                            stInfo.Param = node.Attributes["parameterNames"].Value;
                        }
                        ParseSequence(node.ChildNodes, stInfo);
                    }
                }
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e);
            }
        }

        private static void ParseSequence(XmlNodeList xn, StackTraceInfo stInfo)
        {
            try
            {
                foreach (XmlNode node in xn)
                {
                    if (node.Name == "sequencePoints")
                    {
                        ParseEntry(node.ChildNodes, stInfo);
                    }
                }
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e);
            }
        }

        private static void ParseEntry(XmlNodeList xn, StackTraceInfo stInfo)
        {
            try
            {
                XmlNode bestPointSoFar = null;
                long ilOffset = Convert.ToInt64(stInfo.Offset, 16);
                foreach (XmlNode node in xn)
                {
                    // If the attribute is not 'startLine', but 'hidden', select the best value so far
                    if (Convert.ToInt64(node.Attributes["offset"].Value, 16) > ilOffset)
                    {
                        break;
                    }
                    if (node.Attributes["startLine"] != null)
                    {
                        bestPointSoFar = node;
                    }
                }
                if (bestPointSoFar != null)
                {
                    stInfo.StartLine = bestPointSoFar.Attributes["startLine"].Value;
                    stInfo.EndLine = bestPointSoFar.Attributes["endLine"].Value;
                    stInfo.Document = bestPointSoFar.Attributes["document"].Value;
                }
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e);
            }
        }

        public static Command SymbolicateCommand() =>
            new Command(
                name: "symbolicate", description: "Get the line number from the Method Token and IL Offset in a stacktrace")
            {
                // Handler
                HandlerDescriptor.FromDelegate((SymbolicateDelegate)Symbolicate).GetCommandHandler(),
                // Arguments and Options
                InputArgument(),
                SearchDirectoryOption(),
                OutputOption()
            };

        public static Argument<FileInfo> InputArgument() =>
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

        public static Option<string> OutputOption() =>
            new Option<string>(new[] { "-o", "--output" }, "Output directly to a file")
            {
                Argument = new Argument<string>(name: "output-path")
            };
    }
}