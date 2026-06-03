/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
  
  TurboQuant math operations: random rotation, Lloyd-Max quantization, QJL transform.
  Based on: "TurboQuant: Online Vector Quantization with Near-optimal Distortion Rate" 
  by Zandieh, Daliri, Hadian, Mirrokni (Google Research, 2025)
*/
#if NET6FUNC
using System;
using System.Buffers;
using System.Numerics;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace DBreeze.HNSW
{
    internal static class TurboQuantMath
    {
        #region SIMD Constants
        private static readonly int _vs1f = Vector<float>.Count;
        private static readonly int _vs2f = 2 * Vector<float>.Count;
        private static readonly int _vs3f = 3 * Vector<float>.Count;
        private static readonly int _vs4f = 4 * Vector<float>.Count;

        private static readonly int _vs1d = Vector<double>.Count;
        private static readonly int _vs2d = 2 * Vector<double>.Count;
        private static readonly int _vs3d = 3 * Vector<double>.Count;
        private static readonly int _vs4d = 4 * Vector<double>.Count;
        #endregion

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

        private static readonly ConcurrentDictionary<(int dim, int seed), MatrixCache> _matrixCaches = new ConcurrentDictionary<(int dim, int seed), MatrixCache>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MatrixCache GetOrCreateMatrixCache(int dim, int seed)
        {
            return _matrixCaches.GetOrAdd((dim, seed), key =>
            {
                var rng = CreateRng(key.seed);
                var cache = new MatrixCache
                {
                    HouseholderV_D = new double[key.dim * key.dim],
                    HouseholderV_F = new float[key.dim * key.dim],
                    Factors_D = new double[key.dim],
                    Factors_F = new float[key.dim],
                    QJLMatrix_D = new double[key.dim * key.dim],
                    QJLMatrix_F = new float[key.dim * key.dim]
                };

                // Generate Sequential Householder reflections
                for (int h = 0; h < key.dim; h++)
                {
                    double vnormsq = 0;
                    int baseIdx = h * key.dim;
                    for (int i = 0; i < key.dim; i++)
                    {
                        double val = rng.BoxMuller();
                        cache.HouseholderV_D[baseIdx + i] = val;
                        cache.HouseholderV_F[baseIdx + i] = (float)val;
                        vnormsq += val * val;
                    }
                    cache.Factors_D[h] = vnormsq >= 1e-30 ? (2.0 / vnormsq) : 0;
                    cache.Factors_F[h] = (float)cache.Factors_D[h];
                }

                // Generate QJL S Matrix (Inner-Product Transform stage)
                var rngQjl = CreateRng(key.seed + 1000);
                for (int i = 0; i < key.dim * key.dim; i++)
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

        #region SIMD Dot Product & MultiplyAdd (with Offsets)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double DotProductSIMD(double[] vec, double[] mat, int matOffset, int count)
        {
            double result = 0;
            int offset = 0;

            while (count >= _vs4d)
            {
                result += Vector.Dot(new Vector<double>(vec, offset), new Vector<double>(mat, matOffset + offset));
                result += Vector.Dot(new Vector<double>(vec, offset + _vs1d), new Vector<double>(mat, matOffset + offset + _vs1d));
                result += Vector.Dot(new Vector<double>(vec, offset + _vs2d), new Vector<double>(mat, matOffset + offset + _vs2d));
                result += Vector.Dot(new Vector<double>(vec, offset + _vs3d), new Vector<double>(mat, matOffset + offset + _vs3d));
                count -= _vs4d; offset += _vs4d;
            }
            if (count >= _vs2d)
            {
                result += Vector.Dot(new Vector<double>(vec, offset), new Vector<double>(mat, matOffset + offset));
                result += Vector.Dot(new Vector<double>(vec, offset + _vs1d), new Vector<double>(mat, matOffset + offset + _vs1d));
                count -= _vs2d; offset += _vs2d;
            }
            if (count >= _vs1d)
            {
                result += Vector.Dot(new Vector<double>(vec, offset), new Vector<double>(mat, matOffset + offset));
                count -= _vs1d; offset += _vs1d;
            }
            while (count > 0)
            {
                result += vec[offset] * mat[matOffset + offset];
                offset++; count--;
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DotProductSIMD(float[] vec, float[] mat, int matOffset, int count)
        {
            float result = 0;
            int offset = 0;

            while (count >= _vs4f)
            {
                result += Vector.Dot(new Vector<float>(vec, offset), new Vector<float>(mat, matOffset + offset));
                result += Vector.Dot(new Vector<float>(vec, offset + _vs1f), new Vector<float>(mat, matOffset + offset + _vs1f));
                result += Vector.Dot(new Vector<float>(vec, offset + _vs2f), new Vector<float>(mat, matOffset + offset + _vs2f));
                result += Vector.Dot(new Vector<float>(vec, offset + _vs3f), new Vector<float>(mat, matOffset + offset + _vs3f));
                count -= _vs4f; offset += _vs4f;
            }
            if (count >= _vs2f)
            {
                result += Vector.Dot(new Vector<float>(vec, offset), new Vector<float>(mat, matOffset + offset));
                result += Vector.Dot(new Vector<float>(vec, offset + _vs1f), new Vector<float>(mat, matOffset + offset + _vs1f));
                count -= _vs2f; offset += _vs2f;
            }
            if (count >= _vs1f)
            {
                result += Vector.Dot(new Vector<float>(vec, offset), new Vector<float>(mat, matOffset + offset));
                count -= _vs1f; offset += _vs1f;
            }
            while (count > 0)
            {
                result += vec[offset] * mat[matOffset + offset];
                offset++; count--;
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MultiplyAddSIMD(double[] acc, double[] v, double factor, int dim, int vOffset = 0)
        {
            int count = dim;
            int offset = 0;
            var factorVec = new Vector<double>(factor);

            while (count >= _vs4d)
            {
                (new Vector<double>(acc, offset) + new Vector<double>(v, vOffset + offset) * factorVec).CopyTo(acc, offset);
                (new Vector<double>(acc, offset + _vs1d) + new Vector<double>(v, vOffset + offset + _vs1d) * factorVec).CopyTo(acc, offset + _vs1d);
                (new Vector<double>(acc, offset + _vs2d) + new Vector<double>(v, vOffset + offset + _vs2d) * factorVec).CopyTo(acc, offset + _vs2d);
                (new Vector<double>(acc, offset + _vs3d) + new Vector<double>(v, vOffset + offset + _vs3d) * factorVec).CopyTo(acc, offset + _vs3d);
                count -= _vs4d; offset += _vs4d;
            }
            if (count >= _vs2d)
            {
                (new Vector<double>(acc, offset) + new Vector<double>(v, vOffset + offset) * factorVec).CopyTo(acc, offset);
                (new Vector<double>(acc, offset + _vs1d) + new Vector<double>(v, vOffset + offset + _vs1d) * factorVec).CopyTo(acc, offset + _vs1d);
                count -= _vs2d; offset += _vs2d;
            }
            if (count >= _vs1d)
            {
                (new Vector<double>(acc, offset) + new Vector<double>(v, vOffset + offset) * factorVec).CopyTo(acc, offset);
                count -= _vs1d; offset += _vs1d;
            }
            while (count > 0)
            {
                acc[offset] += v[vOffset + offset] * factor;
                offset++; count--;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MultiplyAddSIMD(float[] acc, float[] v, float factor, int dim, int vOffset = 0)
        {
            int count = dim;
            int offset = 0;
            var factorVec = new Vector<float>(factor);

            while (count >= _vs4f)
            {
                (new Vector<float>(acc, offset) + new Vector<float>(v, vOffset + offset) * factorVec).CopyTo(acc, offset);
                (new Vector<float>(acc, offset + _vs1f) + new Vector<float>(v, vOffset + offset + _vs1f) * factorVec).CopyTo(acc, offset + _vs1f);
                (new Vector<float>(acc, offset + _vs2f) + new Vector<float>(v, vOffset + offset + _vs2f) * factorVec).CopyTo(acc, offset + _vs2f);
                (new Vector<float>(acc, offset + _vs3f) + new Vector<float>(v, vOffset + offset + _vs3f) * factorVec).CopyTo(acc, offset + _vs3f);
                count -= _vs4f; offset += _vs4f;
            }
            if (count >= _vs2f)
            {
                (new Vector<float>(acc, offset) + new Vector<float>(v, vOffset + offset) * factorVec).CopyTo(acc, offset);
                (new Vector<float>(acc, offset + _vs1f) + new Vector<float>(v, vOffset + offset + _vs1f) * factorVec).CopyTo(acc, offset + _vs1f);
                count -= _vs2f; offset += _vs2f;
            }
            if (count >= _vs1f)
            {
                (new Vector<float>(acc, offset) + new Vector<float>(v, vOffset + offset) * factorVec).CopyTo(acc, offset);
                count -= _vs1f; offset += _vs1f;
            }
            while (count > 0)
            {
                acc[offset] += v[vOffset + offset] * factor;
                offset++; count--;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ScaleSIMD(double[] vec, double factor, int dim)
        {
            int count = dim, offset = 0;
            var factorVec = new Vector<double>(factor);
            while (count >= _vs4d)
            {
                (new Vector<double>(vec, offset) * factorVec).CopyTo(vec, offset);
                (new Vector<double>(vec, offset + _vs1d) * factorVec).CopyTo(vec, offset + _vs1d);
                (new Vector<double>(vec, offset + _vs2d) * factorVec).CopyTo(vec, offset + _vs2d);
                (new Vector<double>(vec, offset + _vs3d) * factorVec).CopyTo(vec, offset + _vs3d);
                count -= _vs4d; offset += _vs4d;
            }
            if (count >= _vs2d)
            {
                (new Vector<double>(vec, offset) * factorVec).CopyTo(vec, offset);
                (new Vector<double>(vec, offset + _vs1d) * factorVec).CopyTo(vec, offset + _vs1d);
                count -= _vs2d; offset += _vs2d;
            }
            if (count >= _vs1d) { (new Vector<double>(vec, offset) * factorVec).CopyTo(vec, offset); count -= _vs1d; offset += _vs1d; }
            while (count-- > 0) vec[offset++] *= factor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ScaleSIMD(float[] vec, float factor, int dim)
        {
            int count = dim, offset = 0;
            var factorVec = new Vector<float>(factor);
            while (count >= _vs4f)
            {
                (new Vector<float>(vec, offset) * factorVec).CopyTo(vec, offset);
                (new Vector<float>(vec, offset + _vs1f) * factorVec).CopyTo(vec, offset + _vs1f);
                (new Vector<float>(vec, offset + _vs2f) * factorVec).CopyTo(vec, offset + _vs2f);
                (new Vector<float>(vec, offset + _vs3f) * factorVec).CopyTo(vec, offset + _vs3f);
                count -= _vs4f; offset += _vs4f;
            }
            if (count >= _vs2f)
            {
                (new Vector<float>(vec, offset) * factorVec).CopyTo(vec, offset);
                (new Vector<float>(vec, offset + _vs1f) * factorVec).CopyTo(vec, offset + _vs1f);
                count -= _vs2f; offset += _vs2f;
            }
            if (count >= _vs1f) { (new Vector<float>(vec, offset) * factorVec).CopyTo(vec, offset); count -= _vs1f; offset += _vs1f; }
            while (count-- > 0) vec[offset++] *= factor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double NormSIMD(double[] vec, int dim) => Math.Sqrt(DotProductSIMD(vec, vec, 0, dim));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float NormSIMD(float[] vec, int dim) => (float)Math.Sqrt(DotProductSIMD(vec, vec, 0, dim));

        #endregion

        #region Fast Random Rotation (Cached)
        public static void ApplyRandomRotation(double[] vec, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            for (int h = 0; h < dim; h++)
            {
                double f = cache.Factors_D[h];
                if (f == 0) continue;
                int baseIdx = h * dim;
                double dot = DotProductSIMD(vec, cache.HouseholderV_D, baseIdx, dim);
                MultiplyAddSIMD(vec, cache.HouseholderV_D, -f * dot, dim, baseIdx);
            }
        }

        public static void ApplyInverseRotation(double[] vec, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            for (int h = dim - 1; h >= 0; h--)
            {
                double f = cache.Factors_D[h];
                if (f == 0) continue;
                int baseIdx = h * dim;
                double dot = DotProductSIMD(vec, cache.HouseholderV_D, baseIdx, dim);
                MultiplyAddSIMD(vec, cache.HouseholderV_D, -f * dot, dim, baseIdx);
            }
        }

        public static void ApplyRandomRotation(float[] vec, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            for (int h = 0; h < dim; h++)
            {
                float f = cache.Factors_F[h];
                if (f == 0) continue;
                int baseIdx = h * dim;
                float dot = DotProductSIMD(vec, cache.HouseholderV_F, baseIdx, dim);
                MultiplyAddSIMD(vec, cache.HouseholderV_F, -f * dot, dim, baseIdx);
            }
        }

        public static void ApplyInverseRotation(float[] vec, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            for (int h = dim - 1; h >= 0; h--)
            {
                float f = cache.Factors_F[h];
                if (f == 0) continue;
                int baseIdx = h * dim;
                float dot = DotProductSIMD(vec, cache.HouseholderV_F, baseIdx, dim);
                MultiplyAddSIMD(vec, cache.HouseholderV_F, -f * dot, dim, baseIdx);
            }
        }
        #endregion

        #region Centroid Quantization & Fast QJL Transform
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

        public static byte[] QuantizeCoordinates(float[] rotatedVec, double[] centroids, int dim)
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

        public static sbyte[] QJLQuantize(double[] residual, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            sbyte[] signs = new sbyte[dim];
            for (int i = 0; i < dim; i++)
            {
                double dot = DotProductSIMD(residual, cache.QJLMatrix_D, i * dim, dim);
                signs[i] = (sbyte)(dot >= 0 ? 1 : -1);
            }
            return signs;
        }

        public static double[] QJLDequantize(sbyte[] signs, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            double[] result = new double[dim];
            double scale = Math.Sqrt(Math.PI / 2.0) / dim;
            for (int i = 0; i < dim; i++)
            {
                MultiplyAddSIMD(result, cache.QJLMatrix_D, signs[i], dim, i * dim);
            }
            ScaleSIMD(result, scale, dim);
            return result;
        }

        public static sbyte[] QJLQuantize(float[] residual, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            sbyte[] signs = new sbyte[dim];
            for (int i = 0; i < dim; i++)
            {
                float dot = DotProductSIMD(residual, cache.QJLMatrix_F, i * dim, dim);
                signs[i] = (sbyte)(dot >= 0 ? 1 : -1);
            }
            return signs;
        }

        public static float[] QJLDequantizeF(sbyte[] signs, int dim, int seed)
        {
            var cache = GetOrCreateMatrixCache(dim, seed);
            float[] result = new float[dim];
            float scale = (float)(Math.Sqrt(Math.PI / 2.0) / dim);
            for (int i = 0; i < dim; i++)
            {
                MultiplyAddSIMD(result, cache.QJLMatrix_F, signs[i], dim, i * dim);
            }
            ScaleSIMD(result, scale, dim);
            return result;
        }
        #endregion

        #region End-to-End Handlers (Memory Safe via ArrayPool)
        public static byte[] QuantizeMse(double[] vector, int dim, int bitWidth, int seed, out double norm)
        {
            norm = NormSIMD(vector, dim);
            if (norm > 1e-30) ScaleSIMD(vector, 1.0 / norm, dim);

            ApplyRandomRotation(vector, dim, seed);
            byte[] indices = QuantizeCoordinates(vector, TurboQuantCodebooks.GetCentroids(bitWidth, dim), dim);
            ApplyInverseRotation(vector, dim, seed);

            ScaleSIMD(vector, norm, dim);
            return indices;
        }

        public static double[] DequantizeMse(byte[] indices, int dim, int bitWidth, int seed, double norm)
        {
            double[] result = DequantizeCoordinates(indices, TurboQuantCodebooks.GetCentroids(bitWidth, dim), dim);
            ApplyInverseRotation(result, dim, seed);
            ScaleSIMD(result, norm, dim);
            return result;
        }

        public static byte[] QuantizeMseF(float[] vector, int dim, int bitWidth, int seed, out float norm)
        {
            norm = NormSIMD(vector, dim);
            if (norm > 1e-30f) ScaleSIMD(vector, 1.0f / norm, dim);

            ApplyRandomRotation(vector, dim, seed);
            byte[] indices = QuantizeCoordinates(vector, TurboQuantCodebooks.GetCentroids(bitWidth, dim), dim);
            ApplyInverseRotation(vector, dim, seed);

            ScaleSIMD(vector, norm, dim);
            return indices;
        }

        public static float[] DequantizeMseF(byte[] indices, int dim, int bitWidth, int seed, float norm)
        {
            float[] result = DequantizeCoordinatesF(indices, TurboQuantCodebooks.GetCentroids(bitWidth, dim), dim);
            ApplyInverseRotation(result, dim, seed);
            ScaleSIMD(result, norm, dim);
            return result;
        }

        public static void QuantizeProdSafe(double[] original, int dim, int bitWidth, int seed,
            out byte[] mseIndices, out sbyte[] qjlSigns, out double residualNorm, out double norm)
        {
            double[] unitVec = ArrayPool<double>.Shared.Rent(dim);
            double[] rotatedUnit = ArrayPool<double>.Shared.Rent(dim);
            double[] residual = ArrayPool<double>.Shared.Rent(dim);

            try
            {
                norm = NormSIMD(original, dim);
                Array.Copy(original, unitVec, dim);
                if (norm > 1e-30) ScaleSIMD(unitVec, 1.0 / norm, dim);

                Array.Copy(unitVec, rotatedUnit, dim);
                ApplyRandomRotation(rotatedUnit, dim, seed);

                int mseBits = Math.Max(1, bitWidth - 1);
                mseIndices = QuantizeCoordinates(rotatedUnit, TurboQuantCodebooks.GetCentroids(mseBits, dim), dim);
                ApplyInverseRotation(rotatedUnit, dim, seed);

                for (int i = 0; i < dim; i++) residual[i] = unitVec[i] - rotatedUnit[i];
                residualNorm = NormSIMD(residual, dim);

                if (residualNorm > 1e-30)
                {
                    ScaleSIMD(residual, 1.0 / residualNorm, dim);
                    qjlSigns = QJLQuantize(residual, dim, seed + 1000);
                }
                else
                {
                    qjlSigns = new sbyte[dim];
                    Array.Fill<sbyte>(qjlSigns, 1, 0, dim);
                }
            }
            finally
            {
                ArrayPool<double>.Shared.Return(unitVec);
                ArrayPool<double>.Shared.Return(rotatedUnit);
                ArrayPool<double>.Shared.Return(residual);
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
                MultiplyAddSIMD(result, qjlPart, fullQjlScale, dim);
            }
            return result;
        }

        public static void QuantizeProdSafeF(float[] original, int dim, int bitWidth, int seed,
            out byte[] mseIndices, out sbyte[] qjlSigns, out float residualNorm, out float norm)
        {
            float[] unitVec = ArrayPool<float>.Shared.Rent(dim);
            float[] rotatedUnit = ArrayPool<float>.Shared.Rent(dim);
            float[] residual = ArrayPool<float>.Shared.Rent(dim);

            try
            {
                norm = NormSIMD(original, dim);
                Array.Copy(original, unitVec, dim);
                if (norm > 1e-30f) ScaleSIMD(unitVec, 1.0f / norm, dim);

                Array.Copy(unitVec, rotatedUnit, dim);
                ApplyRandomRotation(rotatedUnit, dim, seed);

                int mseBits = Math.Max(1, bitWidth - 1);
                mseIndices = QuantizeCoordinates(rotatedUnit, TurboQuantCodebooks.GetCentroids(mseBits, dim), dim);
                ApplyInverseRotation(rotatedUnit, dim, seed);

                for (int i = 0; i < dim; i++) residual[i] = unitVec[i] - rotatedUnit[i];
                residualNorm = NormSIMD(residual, dim);

                if (residualNorm > 1e-30f)
                {
                    ScaleSIMD(residual, 1.0f / residualNorm, dim);
                    qjlSigns = QJLQuantize(residual, dim, seed + 1000);
                }
                else
                {
                    qjlSigns = new sbyte[dim];
                    Array.Fill<sbyte>(qjlSigns, 1, 0, dim);
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(unitVec);
                ArrayPool<float>.Shared.Return(rotatedUnit);
                ArrayPool<float>.Shared.Return(residual);
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
                MultiplyAddSIMD(result, qjlPart, fullQjlScale, dim);
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