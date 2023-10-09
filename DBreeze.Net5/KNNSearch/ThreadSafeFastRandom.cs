#if KNNSearch
using System;
using System.Runtime.CompilerServices;

namespace DBreeze.HNSW
{
    internal static class ThreadSafeFastRandom
    {
        private static readonly Random _global = new Random();

        [ThreadStatic]
        private static FastRandom _local;

        private static int GetGlobalSeed()
        {
            int seed;
            lock (_global)
            {
                seed = _global.Next();
            }
            return seed;
        }

        /// <summary>
        /// Returns a non-negative random integer.
        /// </summary>
        /// <returns>A 32-bit signed integer that is greater than or equal to 0 and less than System.Int32.MaxValue.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Next()
        {
            var inst = _local;
            if (inst == null)
            {
                int seed;
                seed = GetGlobalSeed();
                _local = inst = new FastRandom(seed);
            }
            return inst.Next();
        }

        /// <summary>
        /// Returns a non-negative random integer that is less than the specified maximum.
        /// </summary>
        /// <param name="maxValue">The exclusive upper bound of the random number to be generated. maxValue must be greater than or equal to 0.</param>
        /// <returns>A 32-bit signed integer that is greater than or equal to 0, and less than maxValue; that is, the range of return values ordinarily includes 0 but not maxValue. However,
        //  if maxValue equals 0, maxValue is returned.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Next(int maxValue)
        {
            var inst = _local;
            if (inst == null)
            {
                int seed;
                seed = GetGlobalSeed();
                _local = inst = new FastRandom(seed);
            }
            int ans;
            do
            {
                ans = inst.Next(maxValue);
            } while (ans == maxValue);

            return ans;
        }

        /// <summary>
        /// Returns a random integer that is within a specified range.
        /// </summary>
        /// <param name="minValue">The inclusive lower bound of the random number returned.</param>
        /// <param name="maxValue">The exclusive upper bound of the random number returned. maxValue must be greater than or equal to minValue.</param>
        /// <returns>A 32-bit signed integer greater than or equal to minValue and less than maxValue; that is, the range of return values includes minValue but not maxValue. If minValue
        //  equals maxValue, minValue is returned.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Next(int minValue, int maxValue)
        {
            var inst = _local;
            if (inst == null)
            {
                int seed;
                seed = GetGlobalSeed();
                _local = inst = new FastRandom(seed);
            }
            return inst.Next(minValue, maxValue);
        }

        /// <summary>
        /// Generates a random float. Values returned are from 0.0 up to but not including 1.0.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float NextFloat()
        {
            var inst = _local;
            if (inst == null)
            {
                int seed;
                seed = GetGlobalSeed();
                _local = inst = new FastRandom(seed);
            }
            return inst.NextFloat();
        }

        /// <summary>
        /// Fills the elements of a specified array of bytes with random numbers.
        /// </summary>
        /// <param name="buffer">An array of bytes to contain random numbers.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void NextFloats(Span<float> buffer)
        {
            var inst = _local;
            if (inst == null)
            {
                int seed;
                seed = GetGlobalSeed();
                _local = inst = new FastRandom(seed);
            }
            inst.NextFloats(buffer);
        }
    }
}
#endif