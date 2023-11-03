using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DBreeze.Utils;
using static TesterNet6.TextCorpus.Clustering;

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
            //await ITGiantLogotypes.Store_Docs_Vectors();
            //await ITGiantLogotypes.SearchLogo();

            await Store_Furniture_Vectors();
            await SearchFurniture();
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
                    var rowDoc = tran.Select<byte[], string>(tblDocsITLogos, 2.ToIndex(el));
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


            ///*
            //    - Reading data from json, 
            //    - Adding each element as a document 
            //    - Store embedding vectors of each document in a vector table (has own name) for this document type
            // */
            //DBLogotype slt = null;

            //var itg = JsonSerializer.Deserialize<List<ITGiantLogotypesJson>>(File.ReadAllText(@"..\..\..\TextCorpus\ITGiantLogotypes.json"));

            //List<DBLogotype> embeddings = new List<DBLogotype>();

            //foreach (var el in itg)
            //{
            //    //-Getting vector of the text: el.Company + " " + el.LogoDescription
            //    var emb = await OpenAI.GetEmbedding(el.Company + " " + el.LogoDescription);

            //    if (emb != null && !emb.error)
            //    {
            //        slt = new DBLogotype
            //        {
            //            Logotype = el,
            //            Embedding = emb.EmbeddingAnswer.ToArray(), //converting to double[]
            //        };
            //        embeddings.Add(slt);                   
            //    }
            //}

            //File.WriteAllText(@"..\..\..\TextCorpus\ITGiantLogotypesWithEmbeddings.json", JsonSerializer.Serialize(embeddings));



            //-reading DBLogotype with embeddings from the prepared file
            List<DBLogotype> embeddings = JsonSerializer.Deserialize<List<DBLogotype>>(File.ReadAllText(@"..\..\..\TextCorpus\ITGiantLogotypesWithEmbeddings.json"));

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



        //Furniture

        static string tblKNNFurniture = "KNNFurniture"; //Vector Table for ITLogos
        static string tblDocsFurniture = "DocsFurniture"; //Docs ItLogos

        public async static Task Store_Furniture_Vectors()
        {

            //-to skip this run again, checking if data already exists
            using (var tran = Program.DBEngine.GetTransaction())
            {
                int idCnt = tran.Select<byte[], int>(tblDocsFurniture, 1.ToIndex()).Value;
                if (idCnt > 0)
                    return;
            }


            ///*
            //    - Reading data from json, 
            //    - Adding each element as a document 
            //    - Store embedding vectors of each document in a vector table (has own name) for this document type
            // */
            //DBLogotype slt = null;

            //var itg = JsonSerializer.Deserialize<List<ITGiantLogotypesJson>>(File.ReadAllText(@"..\..\..\TextCorpus\ITGiantLogotypes.json"));

            //List<DBLogotype> embeddings = new List<DBLogotype>();

            //foreach (var el in itg)
            //{
            //    //-Getting vector of the text: el.Company + " " + el.LogoDescription
            //    var emb = await OpenAI.GetEmbedding(el.Company + " " + el.LogoDescription);

            //    if (emb != null && !emb.error)
            //    {
            //        slt = new DBLogotype
            //        {
            //            Logotype = el,
            //            Embedding = emb.EmbeddingAnswer.ToArray(), //converting to double[]
            //        };
            //        embeddings.Add(slt);                   
            //    }
            //}

            //File.WriteAllText(@"..\..\..\TextCorpus\ITGiantLogotypesWithEmbeddings.json", JsonSerializer.Serialize(embeddings));



            //-reading DBLogotype with embeddings from the prepared file            
            List <FurnitureV1> embeddings = JsonSerializer.Deserialize<List<FurnitureV1>>(File.ReadAllText(@"..\..\..\TextCorpus\FurnitureV1withEmbeddings.json"));
            //-short vectors from ML.NET / feel the difference
            //List<FurnitureV1> embeddings = JsonSerializer.Deserialize<List<FurnitureV1>>(File.ReadAllText(@"..\..\..\TextCorpus\FurnitureV1withEmbeddings_MSML.json"));
            //-short vectors from ML.NET / feel the difference
            //List<FurnitureV1> embeddings = JsonSerializer.Deserialize<List<FurnitureV1>>(File.ReadAllText(@"..\..\..\TextCorpus\FurnitureV1withLocalEmbeddings.json"));

            //-such format will be inserted into VectorTable, Key is exernal documentID, value is vector itself
            Dictionary<byte[], double[]> vectorsToInsert = new Dictionary<byte[], double[]>();

            if (embeddings.Count > 0)
            {

                using (var tran = Program.DBEngine.GetTransaction())
                {
                    //-sync of Doctable and vector table for searching docs
                    tran.SynchronizeTables(tblKNNFurniture, tblDocsFurniture);

                    //Creating documents of it
                    int idCnt = tran.Select<byte[], int>(tblDocsFurniture, 1.ToIndex()).Value;

                   

                    foreach (var el in embeddings)
                    {
                        foreach (var item in el.Items)
                        {
                            idCnt++;

                            item.Cluster = el.Cluster;

                            //-storing doc itself (not necessary to store embedding vector, it will be stored in vector table, but we do it here for tests, to skip next time acquiring from OpenAI)                      
                            tran.Insert<byte[], string>(tblDocsFurniture, 2.ToIndex(idCnt), JsonSerializer.Serialize(item));
                            //accumulating vectors in Dictionary (bringing toArray el.Value.Item2.ToArray())
                            vectorsToInsert.Add(idCnt.To_4_bytes_array_BigEndian(), item.Embedding);
                        }
                        

                        
                    }

                    //-storing Doc Index monotonically growing
                    if (embeddings.Count > 0)
                    {
                        tran.Insert<byte[], int>(tblDocsFurniture, 1.ToIndex(), idCnt);
                    }

                    //-storing documents as vectors (with/without deferred indexing) 
                    if (vectorsToInsert.Count > 0)
                    {
                        //-in case of big quantity of vectors, use deferredIndexing: true (to run computation in the background)
                        tran.VectorsInsert(tblKNNFurniture, vectorsToInsert, deferredIndexing: false);
                    }

                    tran.Commit();
                }
            }

            //////foreach(var el in vectorsToInsert)
            //////{
            //////    var tmpd= new Dictionary<byte[], double[]>() { { el.Key, el.Value } };
            //////    using (var tran = Program.DBEngine.GetTransaction())
            //////    {

            //////        tran.VectorsInsert(tblKNNFurniture, tmpd, deferredIndexing: false);
            //////        tran.Commit();
            //////    }
            //////}


        }//eof

        /*
         
          static string tblKNNFurniture = "KNNFurniture"; //Vector Table for ITLogos
        static string tblDocsFurniture = "DocsFurniture"; //Docs ItLogos
         */


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async static Task SearchFurniture()
        {

            //Those for tests....
            string question = "soft place to seat";
            question = "a cosy leather sofa";
            //question = "kid's joy";

            //-getting embedding vector for the question from OpenAI
            var emb = await OpenAI.GetEmbedding(question);
            if (emb == null && emb.error)
                throw new Exception("Can't get embedding from the question");
            //-bringing to array
            double[] questionEmbedding = emb.EmbeddingAnswer.ToArray();

            ////-using ML.NET to get short vector
            //MsMLEmbedder embedder = new MsMLEmbedder();
            //double[] questionEmbedding = embedder.GetEmbeddingDoubleArray(question);

            ////-using Local python Embedder short vector
            //var emb = await OpenAI.GetLocalEmbedding(question).ConfigureAwait(false);
            //double[] questionEmbedding = emb.embeddings[0];

            //-show top 3 most relevant answers
            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.ValuesLazyLoadingIsOn = false; //to read key already with value

                var res = tran.VectorsSearchSimilar(tblKNNFurniture, questionEmbedding, 3);

                foreach (var el in res)
                {
                    var rowDoc = tran.Select<byte[], string>(tblDocsFurniture, 2.ToIndex(el));
                    var dbFurniture = JsonSerializer.Deserialize<FurnitureItem>(rowDoc.Value);
                    Console.WriteLine($"Cluster: {dbFurniture.Cluster}; name: {dbFurniture.Name}");
                    Console.WriteLine($"\tDescription: {dbFurniture.Description}");

                }
            }

        }//eof



    }//eoc
}//eon
