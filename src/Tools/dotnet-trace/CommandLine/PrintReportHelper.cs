using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace Microsoft.Diagnostics.Tools.Trace.CommandLine
{
    internal static class PrintReportHelper
    {
        private static string MakeFixedWidth(string text, int width)
        {
            if(text.Length == width)
            {
                return text;
            }
            else if(text.Length > width)
            {
                return text.Substring(0, width);
            }
            else
            {
                return text += new string(' ', width - text.Length);
            }
        }

        internal static void TopNWriteToStdOut(List<CallTreeNodeBase> nodesToReport, bool isInclusive, int n) 
        {
            const int functionColumnWidth = 70;
            const int measureColumnWidth = 20;
            string measureType = null;
            if (isInclusive)
            {
                measureType = "Inclusive";
            }
            else
            {
                measureType = "Exclusive";
            }

            string header = "Top " + n.ToString() + " Functions (" + measureType + ")";
            string uniformHeader = MakeFixedWidth(header, functionColumnWidth+7);

            string inclusive = "Inclusive";
            string uniformInclusive = MakeFixedWidth(inclusive, measureColumnWidth);

            string exclusive = "Exclusive";
            string uniformExclusive = MakeFixedWidth(exclusive, measureColumnWidth);
            Console.WriteLine(uniformHeader + uniformInclusive + uniformExclusive);


            for(int i = 0; i < nodesToReport.Count; i++)
            {
                CallTreeNodeBase node = nodesToReport[i];
                string name = node.Name;
                string uniformName = MakeFixedWidth(name, functionColumnWidth);

                double inclusiveMeasure = Math.Round(node.InclusiveMetricPercent, 2);
                string uniformIMeasure = MakeFixedWidth(inclusiveMeasure.ToString() + "%", measureColumnWidth).PadLeft(measureColumnWidth+4);
                // Console.WriteLine("Inclusive Count is " + node.InclusiveCount);
                double exclusiveMeasure = Math.Round(node.ExclusiveMetricPercent, 2);
                string uniformEMeasure = MakeFixedWidth(exclusiveMeasure.ToString() + "%",measureColumnWidth);

                Console.WriteLine($"{i+1}. " + uniformName + uniformIMeasure + uniformEMeasure);
                
            }
        }
    }
}