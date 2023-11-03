#if NET6FUNC

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
            return 1.0 - DotProduct(ref u, ref v);
            //return 1f - DotProduct(ref u, ref v);
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