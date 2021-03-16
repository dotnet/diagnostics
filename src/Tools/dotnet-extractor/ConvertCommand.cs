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

namespace Microsoft.Diagnostics.Tools.Extractor
{
    internal static class ConvertCommandHandler
    {
        // Temporary folder to store the files converted from pdb to xml
        private static readonly string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        private static string _inputPath;
        private static string _outputPath;
        private static string[] _assemblyPaths;
        private static string[] _pdbPaths;
        private static List<string> _inputString;

        delegate void ConvertDelegate(string input, string assembly, string pdb, string output);

        /// <summary>
        /// .NET Line Number Extractor Tool
        /// </summary>
        /// <param name="input">The input path for file(Exception.log) with exception log</param>
        /// <param name="assembly">All paths in the directory to the assembly where the exception occurred</param>
        /// <param name="pdb">All paths in the directory to the pdb where the exception occurred</param>
        /// <param name="output">The output path for the extracted line number data</param>
        /// <returns></returns>
        private static void Convert(string input, string assembly, string pdb, string output)
        {
            try
            {
                _inputPath = input;
                _outputPath = output;
                _inputString = new List<string>();
                if (_inputPath == null)
                {
                    // If no exception file is entered, the exception log message can be entered directly.
                    Console.Out.WriteLine("Enter the exception log string:");
                    string line;
                    while ((line = Console.ReadLine()) != null && line != "")
                    {
                        if (!line.Contains(":"))
                        {
                            _inputPath = line;
                            _inputString.Clear();
                            break;
                        }
                        _inputString.Add(line);
                    }
                }
                if (_inputString.Count == 0)
                {
                    if (_inputPath == null)
                    {
                        throw new InvalidDataException("Missing exception log string\n");
                    }
                    else if (!File.Exists(_inputPath))
                    {
                        throw new FileNotFoundException("Exception log file not found\n");
                    }
                }
                
                if (assembly != null)
                {
                    _assemblyPaths = assembly.Split(":");
                }
                if (pdb != null)
                {
                    _pdbPaths = pdb.Split(":");
                }
                if (_inputPath != null)
                {
                    _outputPath ??= Path.ChangeExtension(_inputPath, "out");
                }

                _assemblyPaths ??= new string[] { Directory.GetCurrentDirectory() };
                _pdbPaths ??= _assemblyPaths;

                Extractor(PdbToXmlConvert());
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
            }
        }

        private static List<string> PdbToXmlConvert()
        {
            List<string> peFiles = GrabFiles(_assemblyPaths, "*.dll");
            if (peFiles.Count == 0)
            {
                throw new FileNotFoundException("Assembly file not found\n");
            }

            List<string> pdbFiles = GrabFiles(_pdbPaths, "*.pdb");
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

        private static void Extractor(List<string> xmlList)
        {
            if (xmlList.Count == 0)
            {
                RemoveTempDirectory();
                throw new FileNotFoundException("Xml file not found\n");
            }

            GetLineFromLog(xmlList);

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

        private static void GetLineFromLog(List<string> xmlList)
        {
            Console.Out.WriteLine("Extraction result:      ");

            try
            {
                List<string> outputString = new List<string>();
                if (_inputPath == null)
                {
                    // <Exception String>
                    foreach (var line in _inputString)
                    {
                        if (!line.Contains("at "))
                        {
                            continue;
                        }
                        string ret = GetRegex(line, xmlList);
                        outputString.Add(ret);
                        Console.Out.WriteLine(ret);
                    }
                }
                else
                {
                    // <Exception Path>
                    using StreamReader fsr = new StreamReader(new FileStream(_inputPath, FileMode.Open, FileAccess.Read));
                    while (!fsr.EndOfStream)
                    {
                        string line = fsr.ReadLine();
                        if (!line.Contains("at "))
                        {
                            continue;
                        }
                        string ret = GetRegex(line, xmlList);
                        outputString.Add(ret);
                        Console.Out.WriteLine(ret);
                    }
                }

                string output = string.Empty;
                if (_outputPath != null)
                {
                    using StreamWriter fsw = new StreamWriter(new FileStream(_outputPath, FileMode.Create, FileAccess.Write));
                    foreach (var str in outputString)
                    {
                        fsw.WriteLine(str);
                    }
                    output = $"\nOutput: {_outputPath}\n";
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

        public static Command ConvertCommand() =>
            new Command(
                name: "convert", description: "Get the line number from the token value in the stacktrace")
            {
                // Handler
                HandlerDescriptor.FromDelegate((ConvertDelegate)Convert).GetCommandHandler(),
                // Options
                InputOption(),
                AssemblyOption(),
                PdbOption(),
                OutputOption()
            };

        public static Option InputOption() =>
           new Option(
               aliases: new[] { "-i", "--input" },
               description: "Path to the exception log file (File extension: xxxxx.log)")
           {
               Argument = new Argument<string>(name: "input_path")
           };

        public static Option AssemblyOption() =>
            new Option(
                aliases: new[] { "-a", "--assembly" },
                description: "Multiple paths with assembly directories separated by colon(':')")
            {
                Argument = new Argument<string>(name: "path1:path2:...")
            };

        public static Option PdbOption() =>
            new Option(
                aliases: new[] { "-p", "--pdb" },
                description: "Path to the pdb directory (Can be omitted if it is the same as the assembly directory path)")
            {
                Argument = new Argument<string>(name: "path1:path2:...")
            };

        public static Option OutputOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: "Path to the output file (Default: Output to console. If omitted, the xxxxx.out file is created in the same location as the log file)")
            {
                Argument = new Argument<string>(name: "output_path")
            };
    }
}
