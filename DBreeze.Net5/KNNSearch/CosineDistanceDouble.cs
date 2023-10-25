// <copyright file="CosineDistance.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if NET6FUNC
namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Calculates cosine similarity.
    /// </summary>
    /// <remarks>
    /// Intuition behind selecting float as a carrier.
    ///
    /// 1. In practice we work with vectors of dimensionality 100 and each component has value in range [-1; 1]
    ///    There certainly is a possibility of underflow.
    ///    But we assume that such cases are rare and we can rely on such underflow losses.
    ///
    /// 2. According to the article http://www.ti3.tuhh.de/paper/rump/JeaRu13.pdf
    ///    the floating point rounding error is less then 100 * 2^-24 * sqrt(100) * sqrt(100) &lt; 0.0005960
    ///    We deem such precision is satisfactory for out needs.
    /// </remarks>
    internal static class CosineDistanceDouble
    {
        /// <summary>
        /// Calculates cosine distance without making any optimizations.
        /// </summary>
        /// <param name="u">Left vector.</param>
        /// <param name="v">Right vector.</param>
        /// <returns>Cosine distance between u and v.</returns>
        public static double NonOptimized(double[] u, double[] v)
        {
            if (u.Length != v.Length)
            {
                throw new ArgumentException("Vectors have non-matching dimensions");
            }

            double dot = 0.0f;
            double nru = 0.0f;
            double nrv = 0.0f;
            for (int i = 0; i < u.Length; ++i)
            {
                dot += u[i] * v[i];
                nru += u[i] * u[i];
                nrv += v[i] * v[i];
            }

            var similarity = dot / (double)(Math.Sqrt(nru) * Math.Sqrt(nrv));
            return 1 - similarity;
        }

        /// <summary>
        /// Calculates cosine distance with assumption that u and v are unit vectors.
        /// </summary>
        /// <param name="u">Left vector.</param>
        /// <param name="v">Right vector.</param>
        /// <returns>Cosine distance between u and v.</returns>
        public static double ForUnits(double[] u, double[] v)
        {
            if (u.Length!= v.Length)
            {
                throw new ArgumentException("Vectors have non-matching dimensions");
            }

            double dot = 0;
            for (int i = 0; i < u.Length; ++i)
            {
                dot += u[i] * v[i];
            }

            return 1 - dot;
        }

        ///// <summary>
        ///// Calculates cosine distance optimized using SIMD instructions.
        ///// </summary>
        ///// <param name="u">Left vector.</param>
        ///// <param name="v">Right vector.</param>
        ///// <returns>Cosine distance between u and v.</returns>
        //public static double SIMD(double[] u, double[] v)
        //{
        //    if (!Vector.IsHardwareAccelerated)
        //    {
        //        throw new NotSupportedException($"SIMD version of {nameof(CosineDistance)} is not supported");
        //    }

        //    if (u.Length != v.Length)
        //    {
        //        throw new ArgumentException("Vectors have non-matching dimensions");
        //    }

        //    double dot = 0;
        //    var norm = default(Vector2);
        //    //var norm = default(Vector<double>);
        //    int step = Vector<double>.Count;

        //    int i, to = u.Length - step;
        //    for (i = 0; i <= to; i += step)
        //    {
        //        var ui = new Vector<double>(u, i);
        //        var vi = new Vector<double>(v, i);
        //        dot += Vector.Dot(ui, vi);
        //        norm.X += Vector.Dot(ui, ui);
        //        norm.Y += Vector.Dot(vi, vi);
        //    }

        //    for (; i < u.Length; ++i)
        //    {
        //        dot += u[i] * v[i];
        //        norm.X += u[i] * u[i];
        //        norm.Y += v[i] * v[i];
        //    }

        //    norm = Vector2.SquareRoot(norm);
        //    double n = (norm.X * norm.Y);

        //    if (n == 0)
        //    {
        //        return 1f;
        //    }

        //    var similarity = dot / n;
        //    return 1f - similarity;
        //}

        /// <summary>
        /// Use SIMD when you have arbitrary vectors that may not be normalized (unit vectors).
        /// 
        /// Calculates cosine distance optimized using SIMD instructions.
        /// </summary>
        /// <param name="u"></param>
        /// <param name="v"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static double SIMD(double[] u, double[] v)
        {
            if (!Vector.IsHardwareAccelerated)
            {
                throw new NotSupportedException($"SIMD version of {nameof(CosineDistance)} is not supported");
            }

            if (u.Length != v.Length)
            {
                throw new ArgumentException("Vectors have non-matching dimensions");
            }

            double dot = 0;
            double normU = 0;
            double normV = 0;

            int step = Vector<double>.Count;
            int i, to = u.Length - step;

            for (i = 0; i <= to; i += step)
            {
                var ui = new Vector<double>(u, i);
                var vi = new Vector<double>(v, i);
                dot += Vector.Dot(ui, vi);
                normU += Vector.Dot(ui, ui);
                normV += Vector.Dot(vi, vi);
            }

            for (; i < u.Length; ++i)
            {
                dot += u[i] * v[i];
                normU += u[i] * u[i];
                normV += v[i] * v[i];
            }

            double norm = Math.Sqrt(normU * normV);

            if (norm == 0)
            {
                return 1.0;
            }

            var similarity = dot / norm;
            return 1.0 - similarity;
        }

        /// <summary>
        /// Calculates cosine distance with assumption that u and v are unit vectors using SIMD instructions.
        /// </summary>
        /// <param name="u">Left vector.</param>
        /// <param name="v">Right vector.</param>
        /// <returns>Cosine distance between u and v.</returns>
        public static double SIMDForUnits(double[] u, double[] v)
        {
            return 1f - DotProduct(ref u, ref v);
        }

        private static readonly int _vs1 = Vector<double>.Count;
        private static readonly int _vs2 = 2 * Vector<double>.Count;
        private static readonly int _vs3 = 3 * Vector<double>.Count;
        private static readonly int _vs4 = 4 * Vector<double>.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double DotProduct(ref double[] lhs, ref double[] rhs)
        {
            double result = 0f;

            var count = lhs.Length;
            var offset = 0;

            while (count >= _vs4)
            {
                result += Vector.Dot(new Vector<double>(lhs, offset), new Vector<double>(rhs, offset));
                result += Vector.Dot(new Vector<double>(lhs, offset + _vs1), new Vector<double>(rhs, offset + _vs1));
                result += Vector.Dot(new Vector<double>(lhs, offset + _vs2), new Vector<double>(rhs, offset + _vs2));
                result += Vector.Dot(new Vector<double>(lhs, offset + _vs3), new Vector<double>(rhs, offset + _vs3));
                if (count == _vs4) return result;
                count -= _vs4;
                offset += _vs4;
            }

            if (count >= _vs2)
            {
                result += Vector.Dot(new Vector<double>(lhs, offset), new Vector<double>(rhs, offset));
                result += Vector.Dot(new Vector<double>(lhs, offset + _vs1), new Vector<double>(rhs, offset + _vs1));
                if (count == _vs2) return result;
                count -= _vs2;
                offset += _vs2;
            }
            if (count >= _vs1)
            {
                result += Vector.Dot(new Vector<double>(lhs, offset), new Vector<double>(rhs, offset));
                if (count == _vs1) return result;
                count -= _vs1;
                offset += _vs1;
            }
            if (count > 0)
            {
                while (count > 0)
                {
                    result += lhs[offset] * rhs[offset];
                    offset++; count--;
                }
            }
            return result;
        }
    }
}
#endif