using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DBreeze.Utils;

namespace TesterNet6.TextCorpus
{
   

    /// <summary>
    /// 
    /// </summary>
    public static class ITGiantLogotypes
    {        
        static string tblKNNITLogos = "KNNITLogos"; //Vector Table for ITLogos
        static string tblDocsITLogos = "DocsITLogos"; //Docs ItLogos


        public async static Task Example()
        {
            await ITGiantLogotypes.Store_Docs_Vectors();
            await ITGiantLogotypes.SearchLogo();
        }

        public class DBLogotype
        {
            public ITGiantLogotypesJson Logotype { get; set; }
            public double[] Embedding { get; set; }
        }

        public class ITGiantLogotypesJson
        {
            public string Company { get; set; }
            public string LogoDescription { get; set; }

        }

        public async static Task SearchLogo()
        {
            
            //Those for tests....
            string question = "fruit like logo";
            //question = "square like logo";
            question = "rectangle like logo";
            question = "multicolor logo";

            //-getting embedding vector for the question
            var emb = await OpenAI.GetEmbedding(question);
            

            if (emb == null && emb.error)
                throw new Exception("Can't get embedding from the question");

            //-bringing to array
            double[] questionEmbedding = emb.EmbeddingAnswer.ToArray();

            //-show top 3 most relevant answers
            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.ValuesLazyLoadingIsOn = false; //to read key already with value

                var res = tran.VectorsSearchSimilar(tblKNNITLogos, questionEmbedding, 3);

                foreach (var el in res)
                {
                    var rowDoc = tran.Select<byte[], string>(tblDocsITLogos, 2.ToIndex(el.ExternalId));
                    var dbCompany = JsonSerializer.Deserialize<DBLogotype>(rowDoc.Value);
                    Console.WriteLine($"Company: {dbCompany.Logotype.Company}");
                    Console.WriteLine($"\tDescription: {dbCompany.Logotype.LogoDescription}");                    

                }
            }

        }

       
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async static Task Store_Docs_Vectors()
        {

            //-to skip this run again, checking if data already exists
            using (var tran = Program.DBEngine.GetTransaction())
            {
                int idCnt = tran.Select<byte[], int>(tblDocsITLogos, 1.ToIndex()).Value;
                if (idCnt > 0)
                    return;
            }


            /*
                - Reading data from json, 
                - Adding each element as a document 
                - Store embedding vectors of each document in a vector table (has own name) for this document type
             */
            DBLogotype slt = null;

            var itg = JsonSerializer.Deserialize<List<ITGiantLogotypesJson>>(File.ReadAllText(@"..\..\..\TextCorpus\ITGiantLogotypes.json"));

            List<DBLogotype> embeddings = new List<DBLogotype>();

            foreach (var el in itg)
            {
                //-Getting vector of the text: el.Company + " " + el.LogoDescription
                var emb = await OpenAI.GetEmbedding(el.Company + " " + el.LogoDescription);

                if (emb != null && !emb.error)
                {
                    slt = new DBLogotype
                    {
                        Logotype = el,
                        Embedding = emb.EmbeddingAnswer.ToArray(), //converting to double[]
                    };
                    embeddings.Add(slt);                   
                }
            }

            if (embeddings.Count > 0)
            {             

                using (var tran = Program.DBEngine.GetTransaction())
                {
                    //-sync of Doctable and vector table for searching docs
                    tran.SynchronizeTables(tblKNNITLogos, tblDocsITLogos);

                    //Creating documents of it
                    int idCnt = tran.Select<byte[], int>(tblDocsITLogos, 1.ToIndex()).Value;

                    //-such format will be inserted into VectorTable, Key is exernal documentID, value is vector itself
                    Dictionary<byte[], double[]> vectorsToInsert = new Dictionary<byte[], double[]>();

                    foreach (var el in embeddings)
                    {
                        idCnt++;
                        //-storing doc itself (not necessary to store embedding vector, it will be stored in vector table, but we do it here for tests, to skip next time acquiring from OpenAI)                      
                        tran.Insert<byte[], string>(tblDocsITLogos, 2.ToIndex(idCnt), JsonSerializer.Serialize(el));

                        //accumulating vectors in Dictionary (bringing toArray el.Value.Item2.ToArray())
                        vectorsToInsert.Add(idCnt.To_4_bytes_array_BigEndian(), el.Embedding);
                    }

                    //-storing Doc Index monotonically growing
                    if (embeddings.Count > 0)
                    {
                        tran.Insert<byte[], int>(tblDocsITLogos, 1.ToIndex(), idCnt);
                    }

                    //-storing documents as vectors (with/without deferred indexing) 
                    if (vectorsToInsert.Count > 0)
                    {
                        //-in case of big quantity of vectors, use deferredIndexing: true (to run computation in the background)
                        tran.VectorsInsert(tblKNNITLogos, vectorsToInsert, deferredIndexing: false);
                    }

                    tran.Commit();
                }
            }


        }//eof



    }//eoc
}//eon
