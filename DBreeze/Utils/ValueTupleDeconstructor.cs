#if NET472 || NETSTANDARD2_1 || NETCOREAPP2_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBreeze.Utils
{
    internal class ValueTupleDeconstructor
    {
        /*
         * Deconstruction generator
         * 
         * var fl = File.ReadAllText(@"D:\Temp\1\deconstruct.txt");
         * 
         deconstruct.txt:

         static (@ALLXX) Deconstruct@DECN<@ALLXX>(List<object> p)
        {
             return (@ALLXP);
        }


            
            int q = 64;
            StringBuilder final = new StringBuilder();
            StringBuilder sbALLXX = new StringBuilder();
            StringBuilder sbALLXP = new StringBuilder();
            for (int i=1;i<=q;i++)
            {
                for(int j=2;j<=i;j++) //starting from 2
                {
                    sbALLXX.Append( "X" + j);
                    sbALLXP.Append($"(X{j})p[{(j-1)}]");
                    if (j != i)
                    {
                        sbALLXX.Append(",");
                        sbALLXP.Append(",");
                    }

                }

                final.Append(
                fl.ReplaceMultiple(new Dictionary<string, string> { 
                    { "@ALLXX", sbALLXX.ToString() } ,
                    { "@ALLXP", sbALLXP.ToString() } ,
                    { "@DECN", i.ToString() }
                })
                );

                sbALLXX.Clear();
                sbALLXP.Clear();
            }

            File.WriteAllText(@"D:\Temp\1\deconstructFinal.txt",final.ToString());
         */


        //static (X1) Deconstruct1<X1>(List<object> p)
        //{
        //    return ((X1)p[0]);
        //}

        static (X1, X2) Deconstruct2<X1, X2>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1]);
        }

        static (X1, X2, X3) Deconstruct3<X1, X2, X3>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2]);
        }

        static (X1, X2, X3, X4) Deconstruct4<X1, X2, X3, X4>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3]);
        }

        static (X1, X2, X3, X4, X5) Deconstruct5<X1, X2, X3, X4, X5>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3], (X5)p[4]);
        }

        static (X1, X2, X3, X4, X5, X6) Deconstruct6<X1, X2, X3, X4, X5, X6>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3], (X5)p[4], (X6)p[5]);
        }

        static (X1, X2, X3, X4, X5, X6, X7) Deconstruct7<X1, X2, X3, X4, X5, X6, X7>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3], (X5)p[4], (X6)p[5], (X7)p[6]);
        }

        static (X1, X2, X3, X4, X5, X6, X7, X8) Deconstruct8<X1, X2, X3, X4, X5, X6, X7, X8>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3], (X5)p[4], (X6)p[5], (X7)p[6], (X8)p[7]);
        }

        static (X1, X2, X3, X4, X5, X6, X7, X8, X9) Deconstruct9<X1, X2, X3, X4, X5, X6, X7, X8, X9>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3], (X5)p[4], (X6)p[5], (X7)p[6], (X8)p[7], (X9)p[8]);
        }

        static (X1, X2, X3, X4, X5, X6, X7, X8, X9, X10) Deconstruct10<X1, X2, X3, X4, X5, X6, X7, X8, X9, X10>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3], (X5)p[4], (X6)p[5], (X7)p[6], (X8)p[7], (X9)p[8], (X10)p[9]);
        }

        static (X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11) Deconstruct11<X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3], (X5)p[4], (X6)p[5], (X7)p[6], (X8)p[7], (X9)p[8], (X10)p[9], (X11)p[10]);
        }

        static (X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12) Deconstruct12<X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3], (X5)p[4], (X6)p[5], (X7)p[6], (X8)p[7], (X9)p[8], (X10)p[9], (X11)p[10], (X12)p[11]);
        }

        static (X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, X13) Deconstruct13<X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, X13>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3], (X5)p[4], (X6)p[5], (X7)p[6], (X8)p[7], (X9)p[8], (X10)p[9], (X11)p[10], (X12)p[11], (X13)p[12]);
        }

        static (X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, X13, X14) Deconstruct14<X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, X13, X14>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3], (X5)p[4], (X6)p[5], (X7)p[6], (X8)p[7], (X9)p[8], (X10)p[9], (X11)p[10], (X12)p[11], (X13)p[12], (X14)p[13]);
        }

        static (X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, X13, X14, X15) Deconstruct15<X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, X13, X14, X15>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3], (X5)p[4], (X6)p[5], (X7)p[6], (X8)p[7], (X9)p[8], (X10)p[9], (X11)p[10], (X12)p[11], (X13)p[12], (X14)p[13], (X15)p[14]);
        }

        static (X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, X13, X14, X15, X16) Deconstruct16<X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, X13, X14, X15, X16>(List<object> p)
        {
            return ((X1)p[0], (X2)p[1], (X3)p[2], (X4)p[3], (X5)p[4], (X6)p[5], (X7)p[6], (X8)p[7], (X9)p[8], (X10)p[9], (X11)p[10], (X12)p[11], (X13)p[12], (X14)p[13], (X15)p[14], (X16)p[15]);
        }


    }
}
#endif