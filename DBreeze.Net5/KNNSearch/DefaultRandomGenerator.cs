#if NET6FUNC
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace DBreeze.HNSW
{
    internal sealed class DefaultRandomGenerator : IProvideRandomValues
    {
        /// <summary>
        /// This is the default configuration (it supports the optimization process to be executed on multiple threads)
        /// </summary>
        public static DefaultRandomGenerator Instance { get; } = new DefaultRandomGenerator(allowParallel: true);

        /// <summary>
        /// This uses the same random number generator but forces the optimization process to run on a single thread (which may be desirable if multiple requests may be processed concurrently
        /// or if it is otherwise not desirable to let a single request access all of the CPUs)
        /// </summary>
        public static DefaultRandomGenerator DisableThreading { get; } = new DefaultRandomGenerator(allowParallel: false);

        private DefaultRandomGenerator(bool allowParallel) => IsThreadSafe = allowParallel;

        public bool IsThreadSafe { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Next(int minValue, int maxValue) => ThreadSafeFastRandom.Next(minValue, maxValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextFloat() => ThreadSafeFastRandom.NextFloat();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void NextFloats(Span<float> buffer) => ThreadSafeFastRandom.NextFloats(buffer);
    }
}
#endif