using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TesterNet6
{
    internal static class DebugCase1
    {
        static List<(long, float[])> _emb_SD = new List<(long, float[])>(); //[S] second document
        static List<(long, float[])> _emb_FD = new List<(long, float[])>(); //[F] first document

        static float[] _emb_Q = null; //[Q] question
        static long docCounter = 0;
        static string _tblKB_ = "tblKnowledgebase";

        public static void Run()
        {
            LoadEmbeddings();

            Case01();
        }

        private static void Case01()
        {
            Program.DBEngine.Scheme.DeleteTable(_tblKB_);

            Console.WriteLine("====INSERTING FIRST DOCUMENT====");
            //_emb_FD contains only 1 vector
            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.VectorsInsert(_tblKB_, _emb_FD);
                tran.Commit();
            }

            using (var tran = Program.DBEngine.GetTransaction())
            {
                var q = tran.VectorsSearchSimilar(_tblKB_, _emb_Q, quantity: 1000);
                Console.WriteLine("====PART 1====");
                foreach (var pair in q)
                {
                    Console.WriteLine($"ExtId: {pair.externalId}");
                }
                Console.WriteLine("====END PART 1====");
            }

            Console.WriteLine("====INSERTING SECOND DOCUMENT====");
            //_emb_SD contains 93 vectors
            using (var tran = Program.DBEngine.GetTransaction())
            {
                //when inserting all at once - WORKS (final result will return 94 of 94 vectors)
                //tran.VectorsInsert(_tblKB_, _emb_SD);

                //When inserting, one by one - if like this - final result will return only 93 of 94 vextors
                foreach (var el in _emb_SD)
                {
                    tran.VectorsInsert(_tblKB_, new List<(long, float[])> { el }); //all at once - WORKS
                }

                tran.Commit();
            }

            using (var tran = Program.DBEngine.GetTransaction())
            {
                var q = tran.VectorsSearchSimilar(_tblKB_, _emb_Q, quantity: 1000);
                Console.WriteLine("====PART 2====");
                Console.WriteLine($"QCount: {q.Count()}");
                foreach (var pair in q)
                {
                    Console.WriteLine($"ExtId: {pair.externalId}");
                }
                Console.WriteLine("====END PART 2====");
            }

            /*
             *  WHEN INSERTIN ONE BY ONE
             * 
             ====INSERTING FIRST DOCUMENT====
====PART 1====
ExtId: 1
====END PART 1====
====INSERTING SECOND DOCUMENT====
====PART 2====
QCount: 93
ExtId: 34
ExtId: 45
ExtId: 12
ExtId: 91
            ...
             
             */

            /*
             *  WHEN INSERTIN ALL AT ONCE
             * 
             * 
            ====INSERTING FIRST DOCUMENT====
====PART 1====
ExtId: 1
====END PART 1====
====INSERTING SECOND DOCUMENT====
====PART 2====
QCount: 94
ExtId: 1
ExtId: 34
ExtId: 45
ExtId: 12
ExtId: 91
ExtId: 69
ExtId: 23
ExtId: 6
         ...    
             */
        }


        private static void LoadEmbeddings()
        {
            /*
             Read all json files in
             C:\ProgramData\S-TEC GmbH\GpsCarControl\Server\Port_27750\ServerBinary\Modules\GM_PdfProcessor\prt_KnowledgeBaseStorage\13223052642441742620\58\


             */

            var dS = @"D:\VS\DBreezeRealm\TestData\Cases\ProtectedConnections\58\Temp_Embeddings";
            var dF = @"D:\VS\DBreezeRealm\TestData\Cases\ProtectedConnections\57\Temp_Embeddings";
            var fQ = @"D:\VS\DBreezeRealm\TestData\Cases\ProtectedConnections\q.json";


            foreach (var f in Directory.GetFiles(dF))
            {
                docCounter++;
                var t2 = JsonSerializer.Deserialize<D>(File.ReadAllText(f));
                _emb_FD.Add((docCounter, t2.text));
            }

            foreach (var f in Directory.GetFiles(dS))
            {
                docCounter++;
                var t2 = JsonSerializer.Deserialize<D>(File.ReadAllText(f));
                _emb_SD.Add((docCounter, t2.text));
            }



            var t1 = JsonSerializer.Deserialize<Q>(File.ReadAllText(fQ));
            _emb_Q = t1.vector;
        }

        public class D
        {
            public float[] text { get; set; }
        }

        public class Q
        {
            public float[] vector { get; set; }
        }
    }
}
