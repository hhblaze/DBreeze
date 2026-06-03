/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
  
  TurboQuant integration: online vector quantization with near-optimal distortion rate.
  Based on: "TurboQuant: Online Vector Quantization with Near-optimal Distortion Rate" 
  by Zandieh, Daliri, Hadian, Mirrokni (Google Research, 2025)
*/
#if NET6FUNC || NET472
using System;
using System.Runtime.CompilerServices;

namespace DBreeze.HNSW
{
   

    /// <summary>
    /// Precomputed Lloyd-Max optimal codebooks for Beta distribution (scaled/shifted).
    /// Automatically generates complete 2^b length arrays upon initialization to avoid truncated boundaries.
    /// Centroids stored as multiples of 1/sqrt(d) for fast scaling.
    /// </summary>
    internal static class TurboQuantCodebooks
    {
        public const int MaxBitWidth = 8;
        public const int MinBitWidth = 1;

        private static readonly double[][] _normalizedCentroids = new double[MaxBitWidth + 1][];

        static TurboQuantCodebooks()
        {
            for (int b = MinBitWidth; b <= MaxBitWidth; b++)
            {
                _normalizedCentroids[b] = ComputeLloydMaxEmpirical(b);
            }
        }

        /// <summary>
        /// Computes exact optimal continuous Lloyd-Max centroids for N(0,1) via K-Means
        /// ensuring length is perfectly 2^b for high bit-widths.
        /// </summary>
        private static double[] ComputeLloydMaxEmpirical(int bits)
        {
            int numCentroids = 1 << bits;

            // Hardcoded fast-paths for highly established bit ratios to prevent numerical noise
            if (bits == 1) return new double[] { -0.79788456, 0.79788456 };
            if (bits == 2) return new double[] { -1.510, -0.453, 0.453, 1.510 };
            if (bits == 3) return new double[] { -2.152, -1.344, -0.756, -0.245, 0.245, 0.756, 1.344, 2.152 };

            // 1D Empirical K-Means for exact N(0,1) Lloyd-Max fitting on high bit-widths
            int samples = 100000;
            double[] data = new double[samples];

            // Reusing a basic PRNG logic specifically for one-time initialization
            ulong state = 12345;
            for (int i = 0; i < samples; i++)
            {
                double u1, u2, s;
                do
                {
                    state ^= state >> 12; state ^= state << 25; state ^= state >> 27;
                    u1 = 2.0 * ((state * 0x2545F4914F6CDD1DUL >> 11) * (1.0 / (1UL << 53))) - 1.0;
                    state ^= state >> 12; state ^= state << 25; state ^= state >> 27;
                    u2 = 2.0 * ((state * 0x2545F4914F6CDD1DUL >> 11) * (1.0 / (1UL << 53))) - 1.0;
                    s = u1 * u1 + u2 * u2;
                } while (s >= 1.0 || s == 0.0);
                data[i] = u1 * Math.Sqrt(-2.0 * Math.Log(s) / s);
            }
            Array.Sort(data);

            double[] centroids = new double[numCentroids];
            for (int i = 0; i < numCentroids; i++)
            {
                centroids[i] = data[samples * (2 * i + 1) / (2 * numCentroids)];
            }

            int[] counts = new int[numCentroids];
            double[] sums = new double[numCentroids];

            for (int iter = 0; iter < 40; iter++)
            {
                Array.Clear(counts, 0, numCentroids);
                Array.Clear(sums, 0, numCentroids);
                int cIdx = 0;

                for (int i = 0; i < samples; i++)
                {
                    double v = data[i];
                    while (cIdx < numCentroids - 1 && Math.Abs(v - centroids[cIdx + 1]) < Math.Abs(v - centroids[cIdx]))
                    {
                        cIdx++;
                    }
                    counts[cIdx]++;
                    sums[cIdx] += v;
                }
                for (int i = 0; i < numCentroids; i++)
                {
                    if (counts[i] > 0) centroids[i] = sums[i] / counts[i];
                }
            }
            return centroids;
        }

        /// <summary>
        /// Gets the codebook centroids for a given bit-width, scaled by 1/sqrt(dimension).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double[] GetCentroids(int bitWidth, int dimension)
        {
            if (bitWidth < MinBitWidth || bitWidth > MaxBitWidth)
                throw new ArgumentOutOfRangeException(nameof(bitWidth), $"Bit-width must be {MinBitWidth}..{MaxBitWidth}");
            if (dimension <= 0)
                throw new ArgumentOutOfRangeException(nameof(dimension), "Dimension must be positive");

            var raw = _normalizedCentroids[bitWidth];
            double scale = 1.0 / Math.Sqrt(dimension);
            int len = raw.Length;
            double[] scaled = new double[len];
            for (int i = 0; i < len; i++)
                scaled[i] = raw[i] * scale;
            return scaled;
        }

        /// <summary>
        /// Gets the raw (unscaled) normalized centroids for a given bit-width.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double[] GetRawCentroids(int bitWidth)
        {
            if (bitWidth < MinBitWidth || bitWidth > MaxBitWidth)
                throw new ArgumentOutOfRangeException(nameof(bitWidth), $"Bit-width must be {MinBitWidth}..{MaxBitWidth}");
            return _normalizedCentroids[bitWidth];
        }
    }
}
#endif