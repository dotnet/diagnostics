using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.Tests
{
    internal static class Workload
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void DoWork(int nIterations)
        {
            for (int i = 0; i < nIterations; ++i)
            {
                MemoryAccessPerformance();
                BranchPredictionPerformance(seed: i);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double MemoryAccessPerformance()
        {
            var doubles = new double[8 * 1024 * 1024];
            for (int i = 0; i < doubles.Length; i += 100)
                doubles[i] = 2.0;
            for (int i = 0; i < doubles.Length; i += 200)
                doubles[i] *= 3.0;
            for (int i = 0; i < doubles.Length; i += 400)
                doubles[i] *= 5.0;
            for (int i = 0; i < doubles.Length; i += 800)
                doubles[i] *= 7.0;
            for (int i = 0; i < doubles.Length; i += 1600)
                doubles[i] *= 11.0;
            return doubles.Average();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IEnumerable<int> BranchPredictionPerformance(int seed)
        {
            const int nCards = 52;
            var deck = new List<int>(Enumerable.Range(0, nCards));
            var rnd = new Random((int)DateTime.Now.Ticks + seed);

            for (int i = 0; i < deck.Count(); ++i)
            {
                var pos = rnd.Next(nCards);
                if (pos % 3 != 0)
                    pos = rnd.Next(nCards);
                var temp = deck[i];
                deck[i] = deck[pos];
                deck[pos] = temp;
            }

            return deck;
        }
    }
}
