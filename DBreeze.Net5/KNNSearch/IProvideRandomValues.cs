#if KNNSearch
using System;
using System.Runtime.CompilerServices;

namespace DBreeze.HNSW
{
    public interface IProvideRandomValues
    {
        bool IsThreadSafe { get; }

        /// <summary>
        /// Generates a random float. Values returned are from 0.0 up to but not including 1.0.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        float NextFloat();

        /// <summary>
        /// Fills the elements of a specified array of bytes with random numbers.
        /// </summary>
        /// <param name="buffer">An array of bytes to contain random numbers.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void NextFloats(Span<float> buffer);

        /// <summary>
        /// Returns a random integer that is within a specified range.
        /// </summary>
        /// <param name="minValue">The inclusive lower bound of the random number returned.</param>
        /// <param name="maxValue">The exclusive upper bound of the random number returned. maxValue must be greater than or equal to minValue.</param>
        /// <returns>A 32-bit signed integer greater than or equal to minValue and less than maxValue; that is, the range of return values includes minValue but not maxValue. If minValue
        //  equals maxValue, minValue is returned.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int Next(int minValue, int maxValue);
    }
}
#endif