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
            List<int> width= new List<int>() {70, 20};
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
            string uniformHeader = MakeFixedWidth(header, width[0]+7);

            string inclusive = "Inclusive";
            string uniformInclusive = MakeFixedWidth(inclusive, width[1]);

            string exclusive = "Exclusive";
            string uniformExclusive = MakeFixedWidth(exclusive, width[1]);
            Console.WriteLine(uniformHeader + uniformInclusive + uniformExclusive);


            for(int i = 0; i < nodesToReport.Count; i++)
            {
                CallTreeNodeBase node = nodesToReport[i];
                string name = node.Name;
                string uniformName = MakeFixedWidth(name, width[0]);

                double inclusiveMeasure = Math.Round(node.InclusiveMetricPercent, 2);
                string uniformIMeasure = MakeFixedWidth(inclusiveMeasure.ToString() + "%", width[1]).PadLeft(width[1]+4);

                double exclusiveMeasure = Math.Round(node.ExclusiveMetricPercent, 2);
                string uniformEMeasure = MakeFixedWidth(exclusiveMeasure.ToString() + "%", width[1]);

                Console.WriteLine($"{i+1}. " + uniformName + uniformIMeasure + uniformEMeasure);
                
            }
        }
    }
}