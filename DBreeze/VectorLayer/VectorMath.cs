using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DBreeze.VectorLayer
{
    internal static class VectorMath
    {
        /// <summary>
        /// Distance between 2 vectors
        /// </summary>
        /// <param name="u"></param>
        /// <param name="v"></param>
        /// <returns></returns>
        public static double Distance_SIMDForUnits(double[] u, double[] v)
        {
            //return 1f - DotProduct(ref u, ref v);
            return 1.0 - DotProduct(ref u, ref v);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double DotProduct(ref double[] lhs, ref double[] rhs)
        {
            double result = 0f;

            var count = lhs.Length;
            var offset = 0;

            while (count >= 4)
            {
                result += VectorDotProduct(lhs[offset], lhs[offset + 1],
                                           rhs[offset], rhs[offset + 1]);

                result += VectorDotProduct(lhs[offset + 2], lhs[offset + 3],
                                           rhs[offset + 2], rhs[offset + 3]);

                if (count == 4) return result;

                count -= 4;
                offset += 4;
            }

            while (count >= 2)
            {
                result += VectorDotProduct(lhs[offset], lhs[offset + 1],
                                           rhs[offset], rhs[offset + 1]);

                if (count == 2) return result;

                count -= 2;
                offset += 2;
            }

            if (count > 0)
            {
                result += lhs[offset] * rhs[offset];
            }

            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double VectorDotProduct(double vector1X, double vector1Y, double vector2X, double vector2Y)
        {
            return (vector1X * vector2X) + (vector1Y * vector2Y);
        }
    }
}
