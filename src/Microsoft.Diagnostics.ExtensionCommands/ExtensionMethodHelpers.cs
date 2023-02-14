using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    internal static class ExtensionMethodHelpers
    {
        public static string ConvertToHumanReadable(this ulong totalBytes) => ConvertToHumanReadable((double)totalBytes);
        public static string ConvertToHumanReadable(this long totalBytes) => ConvertToHumanReadable((double)totalBytes);

        public static string ConvertToHumanReadable(this double totalBytes)
        {
            double updated = totalBytes;

            updated /= 1024;
            if (updated < 1024)
                return $"{updated:0.00}kb";

            updated /= 1024;
            if (updated < 1024)
                return $"{updated:0.00}mb";

            updated /= 1024;
            return $"{updated:0.00}gb";
        }

        internal static ulong FindMostCommonPointer(this IEnumerable<ulong> enumerable)
            => (from ptr in enumerable
                group ptr by ptr into g
                orderby g.Count() descending
                select g.First()).First();
    }
}
