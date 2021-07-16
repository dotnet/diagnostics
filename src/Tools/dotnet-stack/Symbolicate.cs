// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DiaSymReader.Tools;
using Microsoft.Tools.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Globalization;
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

        delegate void SymbolicateDelegate(FileInfo[] inputPath, string searchDir, string output);

        /// <summary>
        /// Get the line number from the Method Token and IL Offset at the stacktrace
        /// </summary>
        /// <param name="inputPath">The input path for file with stacktrace text</param>
        /// <param name="searchDir">All paths in the directory to the assembly and pdb where the exception occurred</param>
        /// <param name="output">The output path for the extracted line number data</param>
        /// <returns></returns>
        private static void Symbolicate(FileInfo[] inputPath, string searchDir, string output)
        {
            try
            {
                if (inputPath == null)
                {
                    throw new InvalidDataException("Required argument missing for command\n");
                }

                string input = string.Empty;
                if (inputPath.Length > 1)
                {
                    for (int i = 0; i < inputPath.Length; i++)
                    {
                        Console.WriteLine($" {i + 1}. {inputPath[i]}");
                    }
                    Console.Write($"Select one of several stacktrace files [1-{inputPath.Length}]: ");
                    input = inputPath[Int32.Parse(Console.ReadLine()) - 1].FullName;
                }
                else
                {
                    input = inputPath[0].FullName;
                }
                if (!File.Exists(input))
                {
                    throw new FileNotFoundException($"{input} file does not exist\n");
                }

                string[] search_paths = null;
                if (searchDir != null)
                {
                    search_paths = searchDir.Split(";");
                }
                search_paths ??= new string[] { Directory.GetCurrentDirectory() };

                Symbolicator(PdbToXmlConvert(search_paths), input, output);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
            }
        }

        private static List<string> PdbToXmlConvert(string[] searchPaths)
        {
            List<string> peFiles = GrabFiles(searchPaths, "*.dll");
            if (peFiles.Count == 0)
            {
                throw new FileNotFoundException("Assembly file not found\n");
            }

            List<string> pdbFiles = GrabFiles(searchPaths, "*.pdb");
            if (pdbFiles.Count == 0)
            {
                throw new FileNotFoundException("PDB file not found\n");
            }

            Console.Out.Write("Converting pdb to xml...");
            Console.SetCursorPosition(0, Console.CursorTop);

            Directory.CreateDirectory(tempDirectory);

            List<string> xmlList = new List<string>();
            foreach (var pdbFile in pdbFiles)
            {
                foreach (var peFile in peFiles)
                {
                    if (Path.GetFileNameWithoutExtension(peFile) == Path.GetFileNameWithoutExtension(pdbFile))
                    {
                        string xmlPath = Path.Combine(tempDirectory, Path.GetFileName(Path.ChangeExtension(peFile, "xml")));
                        GenXmlFromPdb(peFile, pdbFile, xmlPath);
                        xmlList.Add(xmlPath);
                        break;
                    }
                }
            }
            return xmlList;
        }

        private static List<string> GrabFiles(string[] paths, string searchPattern)
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

        private static void Symbolicator(List<string> xmlList, string inputPath, string outputPath)
        {
            if (xmlList.Count == 0)
            {
                RemoveTempDirectory();
                throw new FileNotFoundException("Xml file not found\n");
            }

            GetLineFromStack(xmlList, inputPath, outputPath);

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

        private static void GetLineFromStack(List<string> xmlList, string inputPath, string outputPath)
        {
            Console.Out.WriteLine("Symbolicate result:     ");

            try
            {
                List<string> outputString = new List<string>();
                using StreamReader fsr = new StreamReader(new FileStream(inputPath, FileMode.Open, FileAccess.Read));
                while (!fsr.EndOfStream)
                {
                    string line = fsr.ReadLine();
                    if (!line.Contains("at ") || !line.Contains("+"))
                    {
                        continue;
                    }
                    string ret = GetRegex(line, xmlList);
                    outputString.Add(ret);
                    Console.Out.WriteLine($"\n{line}");
                    Console.Out.WriteLine($">{ret}");
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
                Console.Out.WriteLine($"{output}");
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
                        ret = $" at {stInfo.Type}.{stInfo.Method}({parameterStr}) in {stInfo.Filepath}:line {stInfo.StartLine}";
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

        private static int HexToInt(string value)
        {
            // strip the leading 0x
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(2);
            }
            return Int32.Parse(value, NumberStyles.HexNumber);
        }

        private static void ParseEntry(XmlNodeList xn, StackTraceInfo stInfo)
        {
            try
            {
                XmlNode bestPointSoFar = null;
                int ilOffset = HexToInt(stInfo.Offset);
                foreach (XmlNode node in xn)
                {
                    // If the attribute is not 'startLine', but 'hidden', select the best value so far
                    if (HexToInt(node.Attributes["offset"].Value) > ilOffset)
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
                name: "symbolicate", description: "Get the line number from the Method Token and IL Offset at the stacktrace")
            {
                // Handler
                HandlerDescriptor.FromDelegate((SymbolicateDelegate)Symbolicate).GetCommandHandler(),
                // Arguments and Options
                InputArgument(),
                SearchDirectoryOption(),
                OutputOption()
            };

        public static Argument InputArgument() =>
            new Argument<FileInfo[]>(name: "input-path")
            {
                Description = "Path to the stacktrace text file"
            }.ExistingOnly();

        public static Option SearchDirectoryOption() =>
            new Option(
                aliases: new[] { "-d", "--search-dir" },
                description: "Path of multiple directories with assembly and pdb separated by semicolon(';')")
            {
                Argument = new Argument<string>(name: "dir1;dir2;...")
            };

        public static Option OutputOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: "Output directly to a file")
            {
                Argument = new Argument<string>(name: "output_path")
            };
    }
}