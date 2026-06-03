/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
  
  TurboQuant math operations: random rotation, Lloyd-Max quantization, QJL transform.
  NET472 version - scalar fallback with loop unrolling and cached matrix generations.
  Based on: "TurboQuant: Online Vector Quantization with Near-optimal Distortion Rate" 
  by Zandieh, Daliri, Hadian, Mirrokni (Google Research, 2025)
*/
#if NET472
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace DBreeze.HNSW
{
    internal static class TurboQuantMath
    {
        #region Global Cache for O(d^2) matrix generation
        internal class MatrixCache
        {
            public double[] HouseholderV_D;
            public float[] HouseholderV_F;
            public double[] Factors_D;
            public float[] Factors_F;
            public double[] QJLMatrix_D;
            public float[] QJLMatrix_F;
        }

        private static readonly ConcurrentDictionary<long, MatrixCache> _matrixCaches = new ConcurrentDictionary<long, MatrixCache>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MatrixCache GetOrCreateMatrixCache(int dim, int seed)
        {
            // Pack dim and seed into a single long key to avoid tuple allocations in .NET Framework
            long key = ((long)dim << 32) | (uint)seed;

            return _matrixCaches.GetOrAdd(key, k =>
            {
                var rng = CreateRng(seed);
                var cache = new MatrixCache
                {
                    HouseholderV_D = new double[dim * dim],
                    HouseholderV_F = new float[dim * dim],
                    Factors_D = new double[dim],
                    Factors_F = new float[dim],
                    QJLMatrix_D = new double[dim * dim],
                    QJLMatrix_F = new float[dim * dim]
                };

                // Generate Sequential Householder reflections
                for (int h = 0; h < dim; h++)
                {
                    double vnormsq = 0;
                    int baseIdx = h * dim;
                    for (int i = 0; i < dim; i++)
                    {
                        double val = rng.BoxMuller();
                        cache.HouseholderV_D[baseIdx + i] = val;
                        cache.HouseholderV_F[baseIdx + i] = (float)val;
                        vnormsq += val * val;
                    }
                    cache.Factors_D[h] = vnormsq >= 1e-30 ? (2.0 / vnormsq) : 0;
                    cache.Factors_F[h] = (float)cache.Factors_D[h];
                }

                // Generate QJL S Matrix
                var rngQjl = CreateRng(seed + 1000);
                for (int i = 0; i < dim * dim; i++)
                {
                    double val = rngQjl.BoxMuller();
                    cache.QJLMatrix_D[i] = val;
                    cache.QJLMatrix_F[i] = (float)val;
                }

                return cache;
            });
        }
        #endregion

        #region Seeded Gaussian RNG (Box-Muller)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static XorShiftRng CreateRng(int seed)
        {
            return new XorShiftRng((ulong)seed);
        }
        #endregion

        #region Fast Random Rotation (Cached with Loop Unrolling)
        public static void ApplyRandomRotation(double[] vec, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            double[] hv = cache.HouseholderV_D;

            for (int h = 0; h < dim; h++)
            {
                double f = cache.Factors_D[h];
                if (f == 0) continue;

                int baseIdx = h * dim;
                double dot = 0;
                int i = 0;
                for (; i <= dim - 4; i += 4)
                {
                    dot += vec[i] * hv[baseIdx + i] +
                           vec[i + 1] * hv[baseIdx + i + 1] +
                           vec[i + 2] * hv[baseIdx + i + 2] +
                           vec[i + 3] * hv[baseIdx + i + 3];
                }
                for (; i < dim; i++) dot += vec[i] * hv[baseIdx + i];

                double mul = -f * dot;
                i = 0;
                for (; i <= dim - 4; i += 4)
                {
                    vec[i] += hv[baseIdx + i] * mul;
                    vec[i + 1] += hv[baseIdx + i + 1] * mul;
                    vec[i + 2] += hv[baseIdx + i + 2] * mul;
                    vec[i + 3] += hv[baseIdx + i + 3] * mul;
                }
                for (; i < dim; i++) vec[i] += hv[baseIdx + i] * mul;
            }
        }

        public static void ApplyInverseRotation(double[] vec, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            double[] hv = cache.HouseholderV_D;

            for (int h = dim - 1; h >= 0; h--)
            {
                double f = cache.Factors_D[h];
                if (f == 0) continue;

                int baseIdx = h * dim;
                double dot = 0;
                int i = 0;
                for (; i <= dim - 4; i += 4)
                {
                    dot += vec[i] * hv[baseIdx + i] +
                           vec[i + 1] * hv[baseIdx + i + 1] +
                           vec[i + 2] * hv[baseIdx + i + 2] +
                           vec[i + 3] * hv[baseIdx + i + 3];
                }
                for (; i < dim; i++) dot += vec[i] * hv[baseIdx + i];

                double mul = -f * dot;
                i = 0;
                for (; i <= dim - 4; i += 4)
                {
                    vec[i] += hv[baseIdx + i] * mul;
                    vec[i + 1] += hv[baseIdx + i + 1] * mul;
                    vec[i + 2] += hv[baseIdx + i + 2] * mul;
                    vec[i + 3] += hv[baseIdx + i + 3] * mul;
                }
                for (; i < dim; i++) vec[i] += hv[baseIdx + i] * mul;
            }
        }

        public static void ApplyRandomRotation(float[] vec, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            float[] hv = cache.HouseholderV_F;

            for (int h = 0; h < dim; h++)
            {
                float f = cache.Factors_F[h];
                if (f == 0) continue;

                int baseIdx = h * dim;
                float dot = 0;
                int i = 0;
                for (; i <= dim - 4; i += 4)
                {
                    dot += vec[i] * hv[baseIdx + i] +
                           vec[i + 1] * hv[baseIdx + i + 1] +
                           vec[i + 2] * hv[baseIdx + i + 2] +
                           vec[i + 3] * hv[baseIdx + i + 3];
                }
                for (; i < dim; i++) dot += vec[i] * hv[baseIdx + i];

                float mul = -f * dot;
                i = 0;
                for (; i <= dim - 4; i += 4)
                {
                    vec[i] += hv[baseIdx + i] * mul;
                    vec[i + 1] += hv[baseIdx + i + 1] * mul;
                    vec[i + 2] += hv[baseIdx + i + 2] * mul;
                    vec[i + 3] += hv[baseIdx + i + 3] * mul;
                }
                for (; i < dim; i++) vec[i] += hv[baseIdx + i] * mul;
            }
        }

        public static void ApplyInverseRotation(float[] vec, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            float[] hv = cache.HouseholderV_F;

            for (int h = dim - 1; h >= 0; h--)
            {
                float f = cache.Factors_F[h];
                if (f == 0) continue;

                int baseIdx = h * dim;
                float dot = 0;
                int i = 0;
                for (; i <= dim - 4; i += 4)
                {
                    dot += vec[i] * hv[baseIdx + i] +
                           vec[i + 1] * hv[baseIdx + i + 1] +
                           vec[i + 2] * hv[baseIdx + i + 2] +
                           vec[i + 3] * hv[baseIdx + i + 3];
                }
                for (; i < dim; i++) dot += vec[i] * hv[baseIdx + i];

                float mul = -f * dot;
                i = 0;
                for (; i <= dim - 4; i += 4)
                {
                    vec[i] += hv[baseIdx + i] * mul;
                    vec[i + 1] += hv[baseIdx + i + 1] * mul;
                    vec[i + 2] += hv[baseIdx + i + 2] * mul;
                    vec[i + 3] += hv[baseIdx + i + 3] * mul;
                }
                for (; i < dim; i++) vec[i] += hv[baseIdx + i] * mul;
            }
        }
        #endregion

        #region Centroid Quantization
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte FindNearestCentroid(double value, double[] centroids, int count)
        {
            int lo = 0;
            int hi = count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (centroids[mid] < value) lo = mid + 1;
                else hi = mid;
            }
            if (lo == 0) return 0;
            if (lo == count) return (byte)(count - 1);
            return (byte)(Math.Abs(value - centroids[lo - 1]) < Math.Abs(value - centroids[lo]) ? (lo - 1) : lo);
        }

        public static byte[] QuantizeCoordinates(double[] rotatedVec, double[] centroids, int dim)
        {
            byte[] indices = new byte[dim];
            int cLen = centroids.Length;
            for (int i = 0; i < dim; i++)
            {
                byte idx = FindNearestCentroid(rotatedVec[i], centroids, cLen);
                indices[i] = idx;
                rotatedVec[i] = centroids[idx];
            }
            return indices;
        }

        public static byte[] QuantizeCoordinatesF(float[] rotatedVec, double[] centroids, int dim)
        {
            byte[] indices = new byte[dim];
            int cLen = centroids.Length;
            for (int i = 0; i < dim; i++)
            {
                byte idx = FindNearestCentroid(rotatedVec[i], centroids, cLen);
                indices[i] = idx;
                rotatedVec[i] = (float)centroids[idx];
            }
            return indices;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double[] DequantizeCoordinates(byte[] indices, double[] centroids, int dim)
        {
            double[] result = new double[dim];
            for (int i = 0; i < dim; i++) result[i] = centroids[indices[i]];
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float[] DequantizeCoordinatesF(byte[] indices, double[] centroids, int dim)
        {
            float[] result = new float[dim];
            for (int i = 0; i < dim; i++) result[i] = (float)centroids[indices[i]];
            return result;
        }
        #endregion

        #region Fast QJL Transform
        public static sbyte[] QJLQuantize(double[] residual, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            double[] qMat = cache.QJLMatrix_D;
            sbyte[] signs = new sbyte[dim];

            for (int i = 0; i < dim; i++)
            {
                int baseIdx = i * dim;
                double dot = 0;
                int j = 0;
                for (; j <= dim - 4; j += 4)
                {
                    dot += residual[j] * qMat[baseIdx + j] +
                           residual[j + 1] * qMat[baseIdx + j + 1] +
                           residual[j + 2] * qMat[baseIdx + j + 2] +
                           residual[j + 3] * qMat[baseIdx + j + 3];
                }
                for (; j < dim; j++) dot += residual[j] * qMat[baseIdx + j];
                signs[i] = (sbyte)(dot >= 0 ? 1 : -1);
            }
            return signs;
        }

        public static double[] QJLDequantize(sbyte[] signs, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            double[] qMat = cache.QJLMatrix_D;
            double[] result = new double[dim];
            double scale = Math.Sqrt(Math.PI / 2.0) / dim;

            for (int i = 0; i < dim; i++)
            {
                double sVal = signs[i];
                int baseIdx = i * dim;
                int j = 0;
                for (; j <= dim - 4; j += 4)
                {
                    result[j] += sVal * qMat[baseIdx + j];
                    result[j + 1] += sVal * qMat[baseIdx + j + 1];
                    result[j + 2] += sVal * qMat[baseIdx + j + 2];
                    result[j + 3] += sVal * qMat[baseIdx + j + 3];
                }
                for (; j < dim; j++) result[j] += sVal * qMat[baseIdx + j];
            }

            for (int j = 0; j < dim; j++) result[j] *= scale;
            return result;
        }

        public static sbyte[] QJLQuantizeF(float[] residual, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            float[] qMat = cache.QJLMatrix_F;
            sbyte[] signs = new sbyte[dim];

            for (int i = 0; i < dim; i++)
            {
                int baseIdx = i * dim;
                float dot = 0;
                int j = 0;
                for (; j <= dim - 4; j += 4)
                {
                    dot += residual[j] * qMat[baseIdx + j] +
                           residual[j + 1] * qMat[baseIdx + j + 1] +
                           residual[j + 2] * qMat[baseIdx + j + 2] +
                           residual[j + 3] * qMat[baseIdx + j + 3];
                }
                for (; j < dim; j++) dot += residual[j] * qMat[baseIdx + j];
                signs[i] = (sbyte)(dot >= 0 ? 1 : -1);
            }
            return signs;
        }

        public static float[] QJLDequantizeF(sbyte[] signs, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            float[] qMat = cache.QJLMatrix_F;
            float[] result = new float[dim];
            float scale = (float)(Math.Sqrt(Math.PI / 2.0) / dim);

            for (int i = 0; i < dim; i++)
            {
                float sVal = signs[i];
                int baseIdx = i * dim;
                int j = 0;
                for (; j <= dim - 4; j += 4)
                {
                    result[j] += sVal * qMat[baseIdx + j];
                    result[j + 1] += sVal * qMat[baseIdx + j + 1];
                    result[j + 2] += sVal * qMat[baseIdx + j + 2];
                    result[j + 3] += sVal * qMat[baseIdx + j + 3];
                }
                for (; j < dim; j++) result[j] += sVal * qMat[baseIdx + j];
            }

            for (int j = 0; j < dim; j++) result[j] *= scale;
            return result;
        }
        #endregion

        #region Utilities
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ComputeNorm(double[] vec, int dim)
        {
            double sum = 0;
            int i = 0;
            for (; i <= dim - 4; i += 4) sum += vec[i] * vec[i] + vec[i + 1] * vec[i + 1] + vec[i + 2] * vec[i + 2] + vec[i + 3] * vec[i + 3];
            for (; i < dim; i++) sum += vec[i] * vec[i];
            return Math.Sqrt(sum);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeNormF(float[] vec, int dim)
        {
            float sum = 0;
            int i = 0;
            for (; i <= dim - 4; i += 4) sum += vec[i] * vec[i] + vec[i + 1] * vec[i + 1] + vec[i + 2] * vec[i + 2] + vec[i + 3] * vec[i + 3];
            for (; i < dim; i++) sum += vec[i] * vec[i];
            return (float)Math.Sqrt(sum);
        }
        #endregion

        #region End-to-End Handlers
        public static byte[] QuantizeMse(double[] vector, int dim, int bitWidth, int seed, out double norm)
        {
            norm = ComputeNorm(vector, dim);
            if (norm > 1e-30)
            {
                double invNorm = 1.0 / norm;
                for (int i = 0; i < dim; i++) vector[i] *= invNorm;
            }

            ApplyRandomRotation(vector, dim, seed);
            byte[] indices = QuantizeCoordinates(vector, TurboQuantCodebooks.GetCentroids(bitWidth, dim), dim);
            ApplyInverseRotation(vector, dim, seed);

            for (int i = 0; i < dim; i++) vector[i] *= norm;
            return indices;
        }

        public static double[] DequantizeMse(byte[] indices, int dim, int bitWidth, int seed, double norm)
        {
            double[] result = DequantizeCoordinates(indices, TurboQuantCodebooks.GetCentroids(bitWidth, dim), dim);
            ApplyInverseRotation(result, dim, seed);
            for (int i = 0; i < dim; i++) result[i] *= norm;
            return result;
        }

        public static byte[] QuantizeMseF(float[] vector, int dim, int bitWidth, int seed, out float norm)
        {
            norm = ComputeNormF(vector, dim);
            if (norm > 1e-30f)
            {
                float invNorm = 1.0f / norm;
                for (int i = 0; i < dim; i++) vector[i] *= invNorm;
            }

            ApplyRandomRotation(vector, dim, seed);
            byte[] indices = QuantizeCoordinatesF(vector, TurboQuantCodebooks.GetCentroids(bitWidth, dim), dim);
            ApplyInverseRotation(vector, dim, seed);

            for (int i = 0; i < dim; i++) vector[i] *= norm;
            return indices;
        }

        public static float[] DequantizeMseF(byte[] indices, int dim, int bitWidth, int seed, float norm)
        {
            float[] result = DequantizeCoordinatesF(indices, TurboQuantCodebooks.GetCentroids(bitWidth, dim), dim);
            ApplyInverseRotation(result, dim, seed);
            for (int i = 0; i < dim; i++) result[i] *= norm;
            return result;
        }

        public static void QuantizeProdSafe(double[] original, int dim, int bitWidth, int seed,
            out byte[] mseIndices, out sbyte[] qjlSigns, out double residualNorm, out double norm)
        {
            double[] unitVec = new double[dim];
            double[] rotatedUnit = new double[dim];
            double[] residual = new double[dim];

            norm = ComputeNorm(original, dim);
            if (norm > 1e-30)
            {
                double invNorm = 1.0 / norm;
                for (int i = 0; i < dim; i++) unitVec[i] = original[i] * invNorm;
            }
            else Array.Copy(original, unitVec, dim);

            Array.Copy(unitVec, rotatedUnit, dim);
            ApplyRandomRotation(rotatedUnit, dim, seed);

            int mseBits = Math.Max(1, bitWidth - 1);
            mseIndices = QuantizeCoordinates(rotatedUnit, TurboQuantCodebooks.GetCentroids(mseBits, dim), dim);
            ApplyInverseRotation(rotatedUnit, dim, seed);

            for (int i = 0; i < dim; i++) residual[i] = unitVec[i] - rotatedUnit[i];
            residualNorm = ComputeNorm(residual, dim);

            if (residualNorm > 1e-30)
            {
                double invResNorm = 1.0 / residualNorm;
                for (int i = 0; i < dim; i++) residual[i] *= invResNorm;
                qjlSigns = QJLQuantize(residual, dim, seed + 1000);
            }
            else
            {
                qjlSigns = new sbyte[dim];
                for (int i = 0; i < dim; i++) qjlSigns[i] = 1;
            }
        }

        public static double[] DequantizeProd(byte[] mseIndices, sbyte[] qjlSigns, int dim, int bitWidth, int seed, double norm, double residualNorm)
        {
            int mseBits = Math.Max(1, bitWidth - 1);
            double[] result = DequantizeMse(mseIndices, dim, mseBits, seed, norm);

            if (residualNorm > 1e-30)
            {
                double[] qjlPart = QJLDequantize(qjlSigns, dim, seed + 1000);
                                
                double fullQjlScale = norm * residualNorm;
                for (int i = 0; i < dim; i++) result[i] += fullQjlScale * qjlPart[i];
            }
            return result;
        }

        public static void QuantizeProdSafeF(float[] original, int dim, int bitWidth, int seed,
            out byte[] mseIndices, out sbyte[] qjlSigns, out float residualNorm, out float norm)
        {
            float[] unitVec = new float[dim];
            float[] rotatedUnit = new float[dim];
            float[] residual = new float[dim];

            norm = ComputeNormF(original, dim);
            if (norm > 1e-30f)
            {
                float invNorm = 1.0f / norm;
                for (int i = 0; i < dim; i++) unitVec[i] = original[i] * invNorm;
            }
            else Array.Copy(original, unitVec, dim);

            Array.Copy(unitVec, rotatedUnit, dim);
            ApplyRandomRotation(rotatedUnit, dim, seed);

            int mseBits = Math.Max(1, bitWidth - 1);
            mseIndices = QuantizeCoordinatesF(rotatedUnit, TurboQuantCodebooks.GetCentroids(mseBits, dim), dim);
            ApplyInverseRotation(rotatedUnit, dim, seed);

            for (int i = 0; i < dim; i++) residual[i] = unitVec[i] - rotatedUnit[i];
            residualNorm = ComputeNormF(residual, dim);

            if (residualNorm > 1e-30f)
            {
                float invResNorm = 1.0f / residualNorm;
                for (int i = 0; i < dim; i++) residual[i] *= invResNorm;
                qjlSigns = QJLQuantizeF(residual, dim, seed + 1000);
            }
            else
            {
                qjlSigns = new sbyte[dim];
                for (int i = 0; i < dim; i++) qjlSigns[i] = 1;
            }
        }

        public static float[] DequantizeProdF(byte[] mseIndices, sbyte[] qjlSigns, int dim, int bitWidth, int seed, float norm, float residualNorm)
        {
            int mseBits = Math.Max(1, bitWidth - 1);
            float[] result = DequantizeMseF(mseIndices, dim, mseBits, seed, norm);

            if (residualNorm > 1e-30f)
            {
                float[] qjlPart = QJLDequantizeF(qjlSigns, dim, seed + 1000);
                                
                float fullQjlScale = norm * residualNorm;
                for (int i = 0; i < dim; i++) result[i] += fullQjlScale * qjlPart[i];
            }
            return result;
        }
        #endregion
    }

    /// <summary>
    /// Deterministic xorshift64* PRNG optimized to cache normal generations natively.
    /// </summary>
    internal struct XorShiftRng
    {
        private ulong _state;
        private double _nextNormal;
        private bool _hasNextNormal;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal XorShiftRng(ulong seed)
        {
            _state = seed != 0 ? seed : 1;
            _nextNormal = 0;
            _hasNextNormal = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ulong Next()
        {
            ulong x = _state;
            x ^= x >> 12; x ^= x << 25; x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double BoxMuller()
        {
            if (_hasNextNormal)
            {
                _hasNextNormal = false;
                return _nextNormal;
            }

            double u1, u2, s;
            do
            {
                u1 = 2.0 * NextDouble() - 1.0;
                u2 = 2.0 * NextDouble() - 1.0;
                s = u1 * u1 + u2 * u2;
            } while (s >= 1.0 || s == 0.0);

            s = Math.Sqrt(-2.0 * Math.Log(s) / s);
            _nextNormal = u2 * s;
            _hasNextNormal = true;
            return u1 * s;
        }
    }
}
#endif