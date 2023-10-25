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
            int batchSize = 1000; //100 (batchSize) documents per round
            byte[][] bt = new byte[batchSize][];
            //for (int i = 0; i < 100; i++)
            //{
            //    bt[i]=new byte[500];
            //    rnd.NextBytes(bt[i]);
            //}

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch sw1 = new System.Diagnostics.Stopwatch();
            sw.Start();
            DateTime now = DateTime.Now;
            for (int j = 0; j < 100; j++)//100 times we insert batch
            {
                Console.Write($"{(j* batchSize + batchSize)} - "); //quantity of documents inside
                
                for (int i = 0; i < batchSize; i++)//inserting 100 vectors of size 500 double
                {
                    bt[i] = new byte[500];
                    //rnd.NextBytes(bt[i]);
                    rnd.NextBytes(bt[i]);
                }
                sw1.Reset();
                sw1.Start();
                using (var tran = Program.DBEngine.GetTransaction())
                {
                    var x = bt.Select((k, v) => 
                    new KeyValuePair<byte[], double[]>(
                        (v+ j*batchSize).ToBytes(), 
                        k.Select(Convert.ToDouble).ToArray()))
                    .ToDictionary(k=>k.Key,v=>v.Value);

                    tran.VectorsInsert(tblLoadV1, x);

                    tran.Commit();
                }
                sw1.Stop();
                Console.WriteLine($"roundMS: {sw1.ElapsedMilliseconds}");
            }
            sw.Stop();
            Console.WriteLine($"MS: {sw.ElapsedMilliseconds}; - {(DateTime.Now - now).TotalMilliseconds}");
                

                



        }





    }
}
