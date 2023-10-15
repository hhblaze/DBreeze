using DBreeze.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TesterNet6.TextCorpus
{
    internal static  class Load
    {
        static string tblLoadV1 = "LoadV1";
        /// <summary>
        /// 
        /// </summary>
        public static void LoadV1()
        {
            var rnd=new Random();
            byte[][] bt = new byte[100][];
            //for (int i = 0; i < 100; i++)
            //{
            //    bt[i]=new byte[500];
            //    rnd.NextBytes(bt[i]);
            //}

            for (int j = 0; j < 100; j++)//100 times
            {
                for (int i = 0; i < 100; i++)//inserting 100 vectors of size 500 double
                {
                    bt[i] = new byte[500];
                    rnd.NextBytes(bt[i]);
                }

                using (var tran = Program.DBEngine.GetTransaction())
                {
                    var x = bt.Select((k, v) => 
                    new KeyValuePair<byte[], double[]>(
                        (v+j*100).ToBytes(), 
                        k.Select(Convert.ToDouble).ToArray()))
                    .ToDictionary(k=>k.Key,v=>v.Value);

                    tran.VectorsInsert(tblLoadV1, x);

                    tran.Commit();
                }
            }
                

                



        }
    }
}
