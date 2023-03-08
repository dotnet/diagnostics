// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace Microsoft.Diagnostics.Tools.Trace.CommandLine
{
    internal static class PrintReportHelper
    {
        private static string MakeFixedWidth(string text, int width)
        {
            if (text.Length == width)
            {
                return text;
            }
            else if (text.Length > width)
            {
                return text.Substring(0, width);
            }
            else
            {
                return text += new string(' ', width - text.Length);
            }
        }

        private static string FormatFunction(string name)
        {
            string classMethod;
            Regex nameRx = new(@"(.*\..*)(\(.*\))");
            Match match = nameRx.Match(name);
            string functionList = match.Groups[1].Value;
            string arguments = match.Groups[2].Value;
            if (functionList == string.Empty && arguments == string.Empty)
            {
                return name;
            }
            string[] usingStatement = functionList.Split(".");
            int length = usingStatement.Length;

            if (length < 2)
            {
                if (length == 1)
                {
                    classMethod = usingStatement[length - 1];
                }
                else
                {
                    classMethod = usingStatement[length];
                }
            }
            else
            {
                classMethod = usingStatement[length - 2] + "." + usingStatement[length - 1];
            }
            return classMethod + arguments;
        }

        public static List<string> SplitInto(string str, int n)
        {
            int length = str.Length;
            if (length < n)
            {
                string shortName = MakeFixedWidth(str, n);

                return new List<string> { shortName };
            }

            if (string.IsNullOrEmpty(str) || n < 1)
            {
                throw new ArgumentException();
            }
            IEnumerable<string> uniformName = Enumerable.Range(0, length / n).Select(i => str.Substring(i * n, n));
            List<string> strList = uniformName.ToList();
            int remainder = (length / n) * n;
            strList.Add(str.Substring(remainder, length - remainder));
            return strList;
        }


        internal static void TopNWriteToStdOut(List<CallTreeNodeBase> nodesToReport, bool isInclusive, bool isVerbose)
        {
            const int functionColumnWidth = 70;
            const int measureColumnWidth = 20;
            string measureType;
            if (isInclusive)
            {
                measureType = "Inclusive";
            }
            else
            {
                measureType = "Exclusive";
            }

            int n = nodesToReport.Count;
            int maxDigit = (int)Math.Log10(n) + 1;
            string extra = new(' ', maxDigit - 1);

            string header = "Top " + n.ToString() + " Functions (" + measureType + ")";
            string uniformHeader = MakeFixedWidth(header, functionColumnWidth + 7);

            string inclusive = "Inclusive";
            string uniformInclusive = MakeFixedWidth(inclusive, measureColumnWidth);

            string exclusive = "Exclusive";
            string uniformExclusive = MakeFixedWidth(exclusive, measureColumnWidth);
            Console.WriteLine(uniformHeader + extra + uniformInclusive + uniformExclusive);

            int numLines;
            for (int i = 0; i < n; i++)
            {

                int iLength = (int)Math.Log10(i + 1) + 1;
                int numSpace = maxDigit - iLength + 1;

                CallTreeNodeBase node = nodesToReport[i];
                string name = node.Name;
                string formatName = FormatFunction(name);
                if (formatName == "?!?")
                {
                    formatName = "Missing Symbol";
                }
                List<string> nameList = SplitInto(formatName, functionColumnWidth);

                if (isVerbose)
                {
                    numLines = nameList.Count;
                }
                else
                {
                    numLines = 1;
                }

                for (int j = 0; j < numLines; j++)
                {
                    string inclusiveMeasure = "";
                    string exclusiveMeasure = "";
                    string number = new(' ', maxDigit + 2); //+2 to account for '. '

                    if (j == 0)
                    {
                        inclusiveMeasure = Math.Round(node.InclusiveMetricPercent, 2).ToString() + "%";
                        exclusiveMeasure = Math.Round(node.ExclusiveMetricPercent, 2).ToString() + "%";
                        number = string.Concat((i + 1).ToString(), ".", number.AsSpan(maxDigit - numSpace + 2));
                    }

                    string uniformIMeasure = MakeFixedWidth(inclusiveMeasure, measureColumnWidth).PadLeft(measureColumnWidth + 4);
                    string uniformEMeasure = MakeFixedWidth(exclusiveMeasure, measureColumnWidth);
                    Console.WriteLine(number + nameList[j] + uniformIMeasure + uniformEMeasure);
                }

            }
        }
    }
}
