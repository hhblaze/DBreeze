/*
  Copyright https://github.com/wlou/HNSW.Net MIT License  
  It's a free software for those who think that it should be free.
*/
#if NET472

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
            return false;
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


        public static float DistanceForUnits(float[] u, float[] v) // Cosine distance
        {
            return DistanceForUnitsSimple(u, v);
            
        }


        public static float[] NormalizeVector(float[] vector)
        {
            return NormalizeSimple(vector);           
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
            

        

        public static double DistanceForUnits(double[] u, double[] v) //Cosine
        {
            return DistanceForUnitsSimple(u, v);
        }

        /// <summary>
        /// Normalize vector
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        public static double[] NormalizeVector(double[] vector)
        {
            return NormalizeSimple(vector);
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

       



    }
}
#endif