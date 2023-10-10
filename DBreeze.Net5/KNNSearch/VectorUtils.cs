// <copyright file="VectorUtils.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// </copyright>

#if KNNSearch
namespace DBreeze.HNSW
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;

    internal static class VectorUtils
    {
        public static float Magnitude(IList<float> vector)
        {
            float magnitude = 0.0f;
            for (int i = 0; i < vector.Count; ++i)
            {
                magnitude += vector[i] * vector[i];
            }

            return (float)Math.Sqrt(magnitude);
        }

        public static void Normalize(IList<float> vector)
        {
            float normFactor = 1 / Magnitude(vector);
            for (int i = 0; i < vector.Count; ++i)
            {
                vector[i] *= normFactor;
            }
        }

        public static float MagnitudeSIMD(float[] vector)
        {
            if (!Vector.IsHardwareAccelerated)
            {
                throw new NotSupportedException($"{nameof(VectorUtils.NormalizeSIMD)} is not supported");
            }

            float magnitude = 0.0f;
            int step = Vector<float>.Count;

            int i, to = vector.Length - step;
            for (i = 0; i <= to; i += Vector<float>.Count)
            {
                var vi = new Vector<float>(vector, i);
                magnitude += Vector.Dot(vi, vi);
            }

            for (; i < vector.Length; ++i)
            {
                magnitude += vector[i] * vector[i];
            }

            return (float)Math.Sqrt(magnitude);
        }

        public static void NormalizeSIMD(float[] vector)
        {
            if (!Vector.IsHardwareAccelerated)
            {
                throw new NotSupportedException($"{nameof(VectorUtils.NormalizeSIMD)} is not supported");
            }

            float normFactor = 1f / MagnitudeSIMD(vector);
            int step = Vector<float>.Count;

            int i, to = vector.Length - step;
            for (i = 0; i <= to; i += step)
            {
                var vi = new Vector<float>(vector, i);
                vi = Vector.Multiply(normFactor, vi);
                vi.CopyTo(vector, i);
            }

            for (; i < vector.Length; ++i)
            {
                vector[i] *= normFactor;
            }
        }
    }
}
#endif