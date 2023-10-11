using DBreeze.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static TesterNet6.TextCorpus.ITGiantLogotypes;

namespace TesterNet6.TextCorpus
{
    internal class Clustering
    {
        static string tblKNNITLogos = "KNNITLogos"; //Vector Table for ITLogos
        static string tblDocsITLogos = "DocsITLogos"; //Docs ItLogos

        public static void KMeansTest()
        {

            //----------FIRST APPROACH

            //-from all items in tblKNNITLogos we try to create 2 random clusters
            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.ValuesLazyLoadingIsOn = false; //to read key already with value

                var res = tran.VectorsClusteringKMeans(tblKNNITLogos, 2);

                foreach (var el in res)
                {
                    foreach (var doc in el.Value)
                    {
                        Console.WriteLine($"CLUSTER: {el.Key}");
                        var rowDoc = tran.Select<byte[], string>(tblDocsITLogos, 2.ToIndex(doc));
                        var dbCompany = JsonSerializer.Deserialize<DBLogotype>(rowDoc.Value);
                        Console.WriteLine($"Company: {dbCompany.Logotype.Company}");
                        Console.WriteLine($"\tDescription: {dbCompany.Logotype.LogoDescription}");

                    }

                }
            }//eo using


            //----------SECOND APPROACH


            //-from all items in tblKNNITLogos we try to create clusters around specifed documents
            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.ValuesLazyLoadingIsOn = false; //to read key already with value

                List<byte[]> twoClusters=new List<byte[]>();
                foreach(var row in tran.SelectForwardStartsWith<byte[], string>(tblDocsITLogos, 2.ToIndex()))
                {
                    var dbCompany = JsonSerializer.Deserialize<DBLogotype>(row.Value);
                    //Console.WriteLine($"{row.Key.Substring(1).To_Int32_BigEndian()} Company: {dbCompany.Logotype.Company}");

                    if (dbCompany.Logotype.Company == "MICROSOFT")
                        twoClusters.Add(row.Key.Substring(1));
                    if (dbCompany.Logotype.Company == "META")
                        twoClusters.Add(row.Key.Substring(1));
                }


                var res = tran.VectorsClusteringKMeans(tblKNNITLogos, 0, externalDocumentIDsAsCentroids: twoClusters);

                foreach (var el in res)
                {
                    foreach (var doc in el.Value)
                    {
                        Console.WriteLine($"CLUSTER: {el.Key}");
                        var rowDoc = tran.Select<byte[], string>(tblDocsITLogos, 2.ToIndex(doc));
                        var dbCompany = JsonSerializer.Deserialize<DBLogotype>(rowDoc.Value);
                        Console.WriteLine($"Company: {dbCompany.Logotype.Company}");
                        Console.WriteLine($"\tDescription: {dbCompany.Logotype.LogoDescription}");

                    }

                }
            }//eo using


        }//eof



        // Root myDeserializedClass = JsonConvert.DeserializeObject<List<Root>>(myJsonResponse);
        public class FurnitureItem
        {
            public string Name { get; set; }
            public string Description { get; set; }

            public double[] Embedding { get; set; }
        }

        public class FurnitureV1
        {
            public string Cluster { get; set; }
            public List<FurnitureItem> Items { get; set; }
        }


        public static async Task KMeansFindCluster()
        {
            //-getting embeddings for each item of @"..\..\..\TextCorpus\FurnitureV1.json", further we will work only with @"..\..\..\TextCorpus\FurnitureV1withEmbeddings.json"
            //await GetFunrnitureV1Embeddings();

            var furnitureLst = JsonSerializer.Deserialize<List<FurnitureV1>>(File.ReadAllText(@"..\..\..\TextCorpus\FurnitureV1withEmbeddings.json"));

            using (var tran = Program.DBEngine.GetTransaction())
            {               
                //Flattering cluster prototypes and itemsTobeClustered
                Dictionary<int, (string ClusterName, FurnitureItem Furniture)> clusterPrototypes = new Dictionary<int, (string clusterName, FurnitureItem)>();
                Dictionary<int, (string ClusterName, FurnitureItem Furniture)> itemsToBeClustered = new Dictionary<int, (string clusterName, FurnitureItem)>();

                //Let's take first element from each Cluster as a Cluster prototype and the rest of elements will have to find to which cluster they belong to
                int i = 0;
                int j = 0;
                foreach (var cluster in furnitureLst)
                {
                    var first = cluster.Items.First();                    
                    clusterPrototypes.Add(i,(cluster.Cluster,first));

                    foreach (var item in cluster.Items.Skip(1)) //skipping first value from the cluster and adding to itemsToBeClustered
                    {
                        itemsToBeClustered.Add(j, (cluster.Cluster, item));                       
                        j++;
                    }
                    i++;
                }

                
                //-supplying two Lists of embeddings, first are prototypes that represent different clusters and second are items to be sparsed between all prototypes clusters
                var res = tran.VectorsClusteringKMeans(
                    clusterPrototypes.Values.Select(r=>r.Item2.Embedding).ToList(), 
                    itemsToBeClustered.Values.Select(r => r.Item2.Embedding).ToList()
                    );

                //resulting output
                i = 0;
                j = 0;
                foreach (var el in res)
                {
                    Console.WriteLine($"CLUSTER: -------------{clusterPrototypes[i].ClusterName}-------------");
                    foreach (var item in el.Value)
                    {
                        Console.WriteLine($"\t OriginalCluster: {itemsToBeClustered[j].ClusterName}; Name: {itemsToBeClustered[j].Furniture.Name}");
                        j++;
                    }
                    i++;
                }

            }


        }



        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static async Task GetFunrnitureV1Embeddings()
        {
            var furnitureLst = JsonSerializer.Deserialize<List<FurnitureV1>>(File.ReadAllText(@"..\..\..\TextCorpus\FurnitureV1.json"));

            foreach (var cluster in furnitureLst)
            {
                foreach (var clusterItem in cluster.Items)
                {
                    var emb = await OpenAI.GetEmbedding(cluster.Cluster + " " + clusterItem.Name + " " + clusterItem.Description);
                    if (emb != null && !emb.error)
                    {
                        clusterItem.Embedding = emb.EmbeddingAnswer.ToArray();
                    }
                    else
                    {

                    }
                }
            }

            File.WriteAllText(@"..\..\..\TextCorpus\FurnitureV1withEmbeddings.json", JsonSerializer.Serialize(furnitureLst));
        }

    }//eoc
}//eon
