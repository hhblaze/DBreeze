using DBreeze.Utils;
using ProtoBuf.WellKnownTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static TesterNet6.TextCorpus.Clustering;

namespace TesterNet6.TextCorpus
{
    internal static  class Load
    {

        public static void TestVectorDBv03_insert_01()
        {
            string dirPath = @"D:\Temp\DBPedia\";

            DBreeze.DBreezeEngine eng = new DBreeze.DBreezeEngine(System.IO.Path.Combine(dirPath, @"prt_Data"));
            //DBreeze.DBreezeEngine engWrite = new DBreeze.DBreezeEngine(System.IO.Path.Combine(dirPath, @"prt_Data_01"));
            string tableEmb = "emb";
            string tableEmbText = "embText";

            string tableTestEmb = "TestEmb";

            int TAKE = 10_000;

            //Func<long, float[]> GetItem = (externalId) =>
            //{

            //    //Vectors inside that storage are already normalized

            //    var ll = tranRead.ValuesLazyLoadingIsOn;
            //    tran.ValuesLazyLoadingIsOn = false;
            //    var row = tranRead.Select<uint, byte[]>(tableEmb, (uint)externalId);
            //    tran.ValuesLazyLoadingIsOn = ll;


            //    if (row.Exists)
            //        return SmallWorld<float[], float>.DecompressF(row.Value);
            //    else
            //        throw new Exception($"- GetItem {externalId} is not found");
            //};

            //var vectorConfig= new DBreeze.Transactions.Transaction.VectorTableParameters<float[]> { 
            
            //     BucketSize=100000,
            //     GetItem = GetItem,
            //     NeighbourSelection = DBreeze.Transactions.Transaction.VectorTableParameters<float[]>.eNeighbourSelectionHeuristic.NeighbourSelectSimple,
            //     QuantityOfLogicalProcessorToCompute = Environment.ProcessorCount
            //};

            Stopwatch stopwatch = new Stopwatch();
            Stopwatch stopwatchBatch = Stopwatch.StartNew();
            System.TimeSpan timeSpanP;

            using (var tranRead = eng.GetTransaction())
            using (var tran = Program.DBEngine.GetTransaction())
            {
                int batchNr = 1;
                int batchSize = 1000;
                foreach (var batch in ByBatch(tranRead, tableEmb, batchSize, skip: 50, take: TAKE))
                {
                    stopwatch.Restart();

                    //graph.AddItems(batch, clearDistanceCache: true);
                    tran.VectorsInsert("myVectorTable", batch, vectorTableParameters: null );

                    stopwatch.Stop();
                    timeSpanP = System.TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds);
                    Debug.WriteLine($"-- {batchSize * batchNr}; BatchTime: {timeSpanP.ToString()}");

                    batchNr++;

                }
                stopwatchBatch.Stop();
                timeSpanP = System.TimeSpan.FromMilliseconds(stopwatchBatch.ElapsedMilliseconds);
                Debug.WriteLine($"-- BatchTime Total time: {timeSpanP.ToString()}");

                stopwatch.Restart();
                tran.Commit();
                stopwatch.Stop();
                timeSpanP = System.TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds);
                Debug.WriteLine($"-- Commit Total time: {timeSpanP.ToString()}");
            }
            eng.Dispose();

        }

        public static void TestVectorDBv03_select_01()
        {
            string dirPath = @"D:\Temp\DBPedia\";

            DBreeze.DBreezeEngine eng = new DBreeze.DBreezeEngine(System.IO.Path.Combine(dirPath, @"prt_Data"));
            //DBreeze.DBreezeEngine engWrite = new DBreeze.DBreezeEngine(System.IO.Path.Combine(dirPath, @"prt_Data_01"));
            string tableEmb = "emb";
            string tableEmbText = "embText";

            string tableTestEmb = "TestEmb";

            int TAKE = 10_000;

            string tblVector = "myVectorTable";

            Stopwatch stopwatch = new Stopwatch();
            Stopwatch stopwatchBatch = Stopwatch.StartNew();
            System.TimeSpan timeSpanP;

            using (var tranRead = eng.GetTransaction())
            using (var tran = Program.DBEngine.GetTransaction())
            {
                //Testing output
                Debug.WriteLine($"-- Total count: {tran.VectorsCount<float[]>(tblVector, vectorTableParameters: null)}");

                foreach (var testRow in tranRead.SelectForward<uint, byte[]>(tableTestEmb).Skip(5).Take(1))
                {
                    var embedding = DecompressF(testRow.Value);

                    stopwatchBatch.Restart();
                    var r1 = tran.VectorsSearchSimilar(tblVector, embedding, quantity:20, vectorTableParameters: null);

                    foreach (var br in r1)
                    {
                        Debug.WriteLine($"[{br.distance}] - [{br.externalId}]");
                    }
                    stopwatchBatch.Stop();
                    timeSpanP = System.TimeSpan.FromMilliseconds(stopwatchBatch.ElapsedMilliseconds);
                    Debug.WriteLine($"-- Search time: {timeSpanP.ToString()}");
                }

            }

            eng.Dispose();
        }


        public static IEnumerable<List<(long, float[])>> ByBatch(DBreeze.Transactions.Transaction tran, string table, int batchSize, int take = int.MaxValue, int skip = 0)
        {
            if (tran.Count(table) == 0)
                yield break;

            int i = 0;
            List<(long, float[])> l = new();
            foreach (var el in tran.SelectForward<uint, byte[]>(table).Skip(skip).Take(take))
            {
                if (i == batchSize)
                {
                    i = 0;
                    yield return l;
                    l.Clear();
                }
                var dec = DecompressF(el.Value);
                l.Add((el.Key, dec));
                i++;
            }

            if (l.Count > 0)
                yield return l;
        }

        public static float[] DecompressF(byte[] compressedData)
        {
            using (var inputStream = new MemoryStream(compressedData))
            using (var decompressionStream = new BrotliStream(inputStream, CompressionMode.Decompress))
            using (var outputStream = new MemoryStream())
            {
                decompressionStream.CopyTo(outputStream);

                byte[] byteArray = outputStream.ToArray();
                float[] floatArray = new float[byteArray.Length / sizeof(float)];
                Buffer.BlockCopy(byteArray, 0, floatArray, 0, byteArray.Length);

                return floatArray;
            }
        }


        static string tblLoadV1 = "LoadV1";

        public static void SelectV1()
        {
            DBreeze.Utils.FastRandom frnd = new DBreeze.Utils.FastRandom();
            int vectorSize = 1536; //OpenAI 1536
            double[] queryVector=new double[vectorSize];
            for (int pp = 0; pp < vectorSize; pp++)
                queryVector[pp] = frnd.NextDouble();


            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.ValuesLazyLoadingIsOn = false; //to read key already with value

                var res = tran.VectorsSearchSimilar(tblLoadV1, queryVector, 3);

                foreach (var el in res)
                {
                    //var rowDoc = tran.Select<byte[], string>(tblDotblLoadV1csFurniture, 2.ToIndex(el));
                    // var dbFurniture = JsonSerializer.Deserialize<FurnitureItem>(rowDoc.Value);
                    //Console.WriteLine($"{el.ToHexFromByteArray()}");
                    Console.WriteLine($"{el}");
                    // Console.WriteLine($"\tDescription: {dbFurniture.Description}");

                }
            }
        }

        static double[] GetRandomEmbedding()
        {
            int size = 1536;
            Random random = new Random();
            double[] randomArray = new double[size];

            for (int i = 0; i < size; i++)
            {
                randomArray[i] = random.NextDouble();
            }
            return randomArray;
            // Now you can use the randomArray
        }

        static int idCnt = 0;
        static string tableVector = "tableVector"; //Vector Table
        public static void Insert01()
        {
            //Debug.Log("Store_String_Vectors Start " + Time.time);
            Console.WriteLine("Store_String_Vectors Start ");

            //-such format will be inserted into VectorTable, Key is exernal documentID, value is vector itself
            Dictionary<long, double[]> vectorsToInsert = new Dictionary<long, double[]>();
            using (var tran = Program.DBEngine.GetTransaction())
            {
                //-sync of Doctable and vector table for searching docs
                tran.SynchronizeTables(tableVector);

                //Loop and insert 10 times the same data
                for (int i = 0; i < 50; i++)
                {
                    idCnt++;

                    vectorsToInsert.Add(idCnt, GetRandomEmbedding());
                }

                //-storing documents as vectors (with/without deferred indexing) 
                if (vectorsToInsert.Count > 0)
                {
                    //-in case of big quantity of vectors, use deferredIndexing: true (to run computation in the background)
                    tran.VectorsInsert(tableVector, vectorsToInsert.Select(r=>(r.Key, r.Value)).ToList());
                }

                tran.Commit();

                //Debug.Log("Vector Count == " + tran.Count(tableVector));
            }

            Console.WriteLine("Store_String_Vectors End ");
        }

        public static void LoadV1()
        {
            //for(int jhz=0;jhz<10;jhz++)
            //    SelectV1();

            //return;
            DBreeze.Utils.FastRandom frnd = new DBreeze.Utils.FastRandom();
            var rnd = new Random();
            int batchSize = 1000; //100 (batchSize) documents per round
            int vectorSize = 1536; //OpenAI 1536
            double[][] bt = new double[batchSize][];
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
                Console.Write($"{(j * batchSize + batchSize)} - "); //quantity of documents inside

                for (int i = 0; i < batchSize; i++)//inserting 100 vectors of size 500 double
                {

                    bt[i] = new double[vectorSize];
                    for (int pp = 0; pp < vectorSize; pp++)
                        bt[i][pp] = frnd.NextDouble();
                    //rnd.NextBytes(bt[i]);
                    //rnd.NextBytes(bt[i]);
                }
                sw1.Reset();
                sw1.Start();
                using (var tran = Program.DBEngine.GetTransaction())
                {
                    //var x = bt.Select((k, v) =>
                    //new KeyValuePair<byte[], double[]>(
                    //    (v + j * batchSize).ToBytes(),
                    //    k.Select(Convert.ToDouble).ToArray()))
                    //.ToDictionary(k => k.Key, v => NormalizeVectors(v.Value));

                    ////world.AddVectors(x);
                    //tran.VectorsInsert(tblLoadV1, x);

                    var x = bt.Select((k, v) =>
                   new KeyValuePair<long, double[]>(
                       (v + j * batchSize),
                       k.Select(Convert.ToDouble).ToArray())).Select(r => (r.Key, r.Value)).ToList();
                   //.ToList(k => k.Key, v => NormalizeVectors(v.Value));

                    //world.AddVectors(x);
                    tran.VectorsInsert(tblLoadV1, x);

                    tran.Commit();
                }
                sw1.Stop();
                Console.WriteLine($"roundMS: {sw1.ElapsedMilliseconds}");
            }
            sw.Stop();
            Console.WriteLine($"MS: {sw.ElapsedMilliseconds}; - {(DateTime.Now - now).TotalMilliseconds}");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="vectors"></param>
        /// <returns></returns>
        public static List<double[]> NormalizeVectors(List<double[]> vectors)
        {
            for (int i = 0; i < vectors.Count; i++)
                vectors[i] = NormalizeVectors(vectors[i]);

            return vectors;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double[] NormalizeVectors(double[] vector)
        {
            double magnitude = Math.Sqrt(DotProduct(ref vector, ref vector));
            for (int j = 0; j < vector.Length; j++)
            {
                vector[j] /= magnitude;
            }

            return vector;
        }

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

        ///// <summary>
        ///// 
        ///// </summary>
        //public static void LoadV2()
        //{
        //    var rnd=new Random();
        //    int batchSize = 1000; //100 (batchSize) documents per round
        //    byte[][] bt = new byte[batchSize][];
        //    //for (int i = 0; i < 100; i++)
        //    //{
        //    //    bt[i]=new byte[500];
        //    //    rnd.NextBytes(bt[i]);
        //    //}

        //    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        //    System.Diagnostics.Stopwatch sw1 = new System.Diagnostics.Stopwatch();
        //    sw.Start();
        //    DateTime now = DateTime.Now;
        //    for (int j = 0; j < 100; j++)//100 times we insert batch
        //    {
        //        Console.Write($"{(j* batchSize + batchSize)} - "); //quantity of documents inside

        //        for (int i = 0; i < batchSize; i++)//inserting 100 vectors of size 500 double
        //        {
        //            bt[i] = new byte[500];
        //            //rnd.NextBytes(bt[i]);
        //            rnd.NextBytes(bt[i]);
        //        }
        //        sw1.Reset();
        //        sw1.Start();
        //        using (var tran = Program.DBEngine.GetTransaction())
        //        {
        //            var x = bt.Select((k, v) => 
        //            new KeyValuePair<byte[], double[]>(
        //                (v+ j*batchSize).ToBytes(), 
        //                k.Select(Convert.ToDouble).ToArray()))
        //            .ToDictionary(k=>k.Key,v=>v.Value);

        //            tran.VectorsInsert(tblLoadV1, x);

        //            tran.Commit();
        //        }
        //        sw1.Stop();
        //        Console.WriteLine($"roundMS: {sw1.ElapsedMilliseconds}");
        //    }
        //    sw.Stop();
        //    Console.WriteLine($"MS: {sw.ElapsedMilliseconds}; - {(DateTime.Now - now).TotalMilliseconds}");






        //}





    }
}
