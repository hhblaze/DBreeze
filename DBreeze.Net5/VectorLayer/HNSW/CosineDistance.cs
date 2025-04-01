/*
  Copyright https://github.com/wlou/HNSW.Net MIT License
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/

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
    internal static class CosineDistance
    {
        public static bool IsHardwareAccelerated()
        {
            return Vector.IsHardwareAccelerated;
        }
        

        /// <summary>
        /// Calculates cosine distance with assumption that u and v are unit vectors.
        /// </summary>
        /// <param name="u">Left vector.</param>
        /// <param name="v">Right vector.</param>
        /// <returns>Cosine distance between u and v.</returns>
        //public static float ForUnits(IReadOnlyList<float> u, IReadOnlyList<float> v)
        public static float DistanceForUnitsSimple(float[] u, float[] v)
        {
            //if (u.Length != v.Length)
            //{
            //    throw new ArgumentException("Vectors have non-matching dimensions");
            //}

            float dot = 0;
            for (int i = 0; i < u.Length; ++i)
            {
                dot += u[i] * v[i];
            }

            return 1 - dot;
        }

        public static double DistanceForUnitsSimple(double[] u, double[] v)
        {
            //if (u.Length != v.Length)
            //{
            //    throw new ArgumentException("Vectors have non-matching dimensions");
            //}

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
        //public static float SIMD(float[] u, float[] v)
        //{
        //    if (!Vector.IsHardwareAccelerated)
        //    {
        //        throw new NotSupportedException($"SIMD version of {nameof(CosineDistance)} is not supported");
        //    }

        //    if (u.Length != v.Length)
        //    {
        //        throw new ArgumentException("Vectors have non-matching dimensions");
        //    }

        //    float dot = 0;
        //    var norm = default(Vector2);
        //    int step = Vector<float>.Count;

        //    int i, to = u.Length - step;
        //    for (i = 0; i <= to; i += step)
        //    {
        //        var ui = new Vector<float>(u, i);
        //        var vi = new Vector<float>(v, i);
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
        //    var similarity = dot / (norm.X * norm.Y);
        //    return 1 - similarity;
        //}

        ///// <summary>
        ///// Calculates cosine distance with assumption that u and v are unit vectors using SIMD instructions.
        ///// </summary>
        ///// <param name="u">Left vector.</param>
        ///// <param name="v">Right vector.</param>
        ///// <returns>Cosine distance between u and v.</returns>
        //public static float SIMDForUnits(float[] u, float[] v)
        //{
        //    if (!Vector.IsHardwareAccelerated)
        //    {
        //        throw new NotSupportedException($"SIMD version of {nameof(CosineDistance)} is not supported");
        //    }

        //    if (u.Length != v.Length)
        //    {
        //        throw new ArgumentException("Vectors have non-matching dimensions");
        //    }

        //    float dot = 0;
        //    int step = Vector<float>.Count;

        //    int i, to = u.Length - step;
        //    for (i = 0; i <= to; i += step)
        //    {
        //        var ui = new Vector<float>(u, i);
        //        var vi = new Vector<float>(v, i);
        //        dot += Vector.Dot(ui, vi);
        //    }

        //    for (; i < u.Length; ++i)
        //    {
        //        dot += u[i] * v[i];
        //    }

        //    return 1 - dot;
        //}

        private static readonly int _vs1f = Vector<float>.Count;
        private static readonly int _vs2f = 2 * Vector<float>.Count;
        private static readonly int _vs3f = 3 * Vector<float>.Count;
        private static readonly int _vs4f = 4 * Vector<float>.Count;

        
        public static float DistanceForUnits(float[] u, float[] v) // Cosine distance
        {            
            return 1.0f - DotProduct(u, v); // Makes closest vectors tending to 0, farthest tending to 2
        }


        public static float[] NormalizeVector(float[] vector)
        {
            float magnitude = (float)Math.Sqrt(DotProduct(vector, vector));

            if (magnitude < 1e-10) 
            {
                return vector; 
            }
            for (int j = 0; j < vector.Length; j++)
            {
                vector[j] /= magnitude;
            }

            return vector;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Magnitude(float[] vector)
        {
            float magnitude = 0.0f;
            for (int i = 0; i < vector.Length; ++i)
            {
                magnitude += vector[i] * vector[i];
            }

            return (float)Math.Sqrt(magnitude);
        }

        /// <summary>
        /// Turns vector to unit vector.
        /// </summary>
        /// <param name="vector">The vector to normalize.</param>
        public static float[] NormalizeSimple(float[] vector)
        {
            float normFactor = 1 / Magnitude(vector);
            for (int i = 0; i < vector.Length; ++i)
            {
                vector[i] *= normFactor;
            }
            return vector;
        }

      

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static float[] NormalizeVector(float[] vector)
        //{
        //    float magnitudeSquared = DotProduct(vector, vector);
        //    if (magnitudeSquared < 1e-10f) // Or your chosen small tolerance
        //    {
        //        return vector; // Avoid 0 division; return original vector (or a zero vector, depending on your needs)
        //    }

        //    float magnitude = MathF.Sqrt(magnitudeSquared);
        //    float invMagnitude = 1.0f / magnitude;  // Calculate inverse once

        //    float[] result = new float[vector.Length]; // Create a new array for the result
        //    int count = vector.Length;
        //    int offset = 0;


        //    while (count >= _vs4)
        //    {
        //        var v = new Vector<float>(vector, offset) * invMagnitude;
        //        v.CopyTo(result, offset);

        //        v = new Vector<float>(vector, offset + _vs1) * invMagnitude;
        //        v.CopyTo(result, offset + _vs1);

        //        v = new Vector<float>(vector, offset + _vs2) * invMagnitude;
        //        v.CopyTo(result, offset + _vs2);

        //        v = new Vector<float>(vector, offset + _vs3) * invMagnitude;
        //        v.CopyTo(result, offset + _vs3);

        //        count -= _vs4;
        //        offset += _vs4;
        //    }

        //    if (count >= _vs2)
        //    {
        //        var v = new Vector<float>(vector, offset) * invMagnitude;
        //        v.CopyTo(result, offset);

        //        v = new Vector<float>(vector, offset + _vs1) * invMagnitude;
        //        v.CopyTo(result, offset + _vs1);

        //        count -= _vs2;
        //        offset += _vs2;
        //    }

        //    if (count >= _vs1)
        //    {
        //        var v = new Vector<float>(vector, offset) * invMagnitude;
        //        v.CopyTo(result, offset);

        //        count -= _vs1;
        //        offset += _vs1;
        //    }

        //    //Scalar fallback
        //    if (count > 0)
        //    {
        //        while (count > 0)
        //        {
        //            result[offset] = vector[offset] * invMagnitude;
        //            offset++; count--;
        //        }
        //    }

        //    return result;
        //}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DotProduct(float[] lhs, float[] rhs)
        {
            float result = 0f;

            var count = lhs.Length;
            var offset = 0;

            while (count >= _vs4f)
            {
                result += Vector.Dot(new Vector<float>(lhs, offset), new Vector<float>(rhs, offset));
                result += Vector.Dot(new Vector<float>(lhs, offset + _vs1f), new Vector<float>(rhs, offset + _vs1f));
                result += Vector.Dot(new Vector<float>(lhs, offset + _vs2f), new Vector<float>(rhs, offset + _vs2f));
                result += Vector.Dot(new Vector<float>(lhs, offset + _vs3f), new Vector<float>(rhs, offset + _vs3f));
                if (count == _vs4f) return result;
                count -= _vs4f;
                offset += _vs4f;
            }

            if (count >= _vs2f)
            {
                result += Vector.Dot(new Vector<float>(lhs, offset), new Vector<float>(rhs, offset));
                result += Vector.Dot(new Vector<float>(lhs, offset + _vs1f), new Vector<float>(rhs, offset + _vs1f));
                if (count == _vs2f) return result;
                count -= _vs2f;
                offset += _vs2f;
            }
            if (count >= _vs1f)
            {
                result += Vector.Dot(new Vector<float>(lhs, offset), new Vector<float>(rhs, offset));
                if (count == _vs1f) return result;
                count -= _vs1f;
                offset += _vs1f;
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



        private static readonly int _vs1 = Vector<double>.Count;
        private static readonly int _vs2 = 2 * Vector<double>.Count;
        private static readonly int _vs3 = 3 * Vector<double>.Count;
        private static readonly int _vs4 = 4 * Vector<double>.Count;

        public static double DistanceForUnits(double[] u, double[] v) //Cosine
        {
            return 1.0 - DotProduct( u,  v); //makes closest vectors tending to 0, farthest tending to 2        
        }

        /// <summary>
        /// Normalize vector
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        public static double[] NormalizeVector(double[] vector)
        {
            double magnitude = Math.Sqrt(DotProduct(vector, vector));
            if (magnitude < 1e-10) // Or your chosen small tolerance
            {
                return vector; //Avoid 0 division; return original vector
            }
            for (int j = 0; j < vector.Length; j++)
            {
                vector[j] /= magnitude;
            }

            return vector;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Magnitude(double[] vector)
        {
            double magnitude = 0.0;
            for (int i = 0; i < vector.Length; ++i)
            {
                magnitude += vector[i] * vector[i];
            }

            return (float)Math.Sqrt(magnitude);
        }

        public static double[] NormalizeSimple(double[] vector)
        {
            double normFactor = 1 / Magnitude(vector);
            for (int i = 0; i < vector.Length; ++i)
            {
                vector[i] *= normFactor;
            }
            return vector;
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static double[] NormalizeVector(double[] vector)
        //{
        //    double magnitudeSquared = DotProduct( vector,  vector);
        //    if (magnitudeSquared < 1e-10) // Or your chosen small tolerance
        //    {
        //        return vector; // Avoid 0 division; return original vector (or a zero vector)
        //    }

        //    double magnitude = Math.Sqrt(magnitudeSquared);
        //    double invMagnitude = 1.0 / magnitude;  // Calculate inverse once

        //    double[] result = new double[vector.Length]; // Create new array
        //    int count = vector.Length;
        //    int offset = 0;

        //    while (count >= _vs4)
        //    {
        //        var v = new Vector<double>(vector, offset) * invMagnitude;
        //        v.CopyTo(result, offset);

        //        v = new Vector<double>(vector, offset + _vs1) * invMagnitude;
        //        v.CopyTo(result, offset + _vs1);

        //        v = new Vector<double>(vector, offset + _vs2) * invMagnitude;
        //        v.CopyTo(result, offset + _vs2);

        //        v = new Vector<double>(vector, offset + _vs3) * invMagnitude;
        //        v.CopyTo(result, offset + _vs3);

        //        count -= _vs4;
        //        offset += _vs4;
        //    }
        //    if (count >= _vs2)
        //    {
        //        var v = new Vector<double>(vector, offset) * invMagnitude;
        //        v.CopyTo(result, offset);

        //        v = new Vector<double>(vector, offset + _vs1) * invMagnitude;
        //        v.CopyTo(result, offset + _vs1);

        //        count -= _vs2;
        //        offset += _vs2;
        //    }

        //    if (count >= _vs1)
        //    {
        //        var v = new Vector<double>(vector, offset) * invMagnitude;
        //        v.CopyTo(result, offset);
        //        count -= _vs1;
        //        offset += _vs1;

        //    }

        //    // Scalar fallback
        //    if (count > 0)
        //    {
        //        while (count > 0)
        //        {
        //            result[offset] = vector[offset] * invMagnitude;
        //            offset++;
        //            count--;
        //        }
        //    }

        //    return result;
        //}



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double DotProduct( double[] lhs,  double[] rhs)
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