namespace Microsoft.Diagnostics.Tools.Analyze
{
    internal static class Utils
    {
        internal static string FormatAddress(uint addr)
        {
            var hi = (short)(addr >> 16);
            var lo = (short)addr;
            return $"{hi:x4}`{lo:x4}";
        }

        internal static string FormatAddress(ulong addr)
        {
            var hi = (int)(addr >> 32);
            var lo = (int)addr;
            return $"{hi:x8}`{lo:x8}";
        }
    }
}
