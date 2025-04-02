using DBreeze.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static TesterNet6.OpenAI;
using System.Web;
using static TesterNet6.TextCorpus.ITGiantLogotypes;


namespace TesterNet6.TextCorpus
{
    internal class Clustering
    {
        static string tblKNNITLogos = "KNNITLogos"; //Vector Table for ITLogos
        static string tblDocsITLogos = "DocsITLogos"; //Docs ItLogos

        static string tblKNNFurniture = "KNNFurniture"; //Vector Table for ITLogos
        static string tblDocsFurniture = "DocsFurniture"; //Docs ItLogos

        public static void KMeansTest()
        {

            //////----------FIRST APPROACH - not possible anymore to split complete table, security reasons, use other overloads of VectorsClusteringKMeans

            //////-from all items in tblKNNITLogos we try to create 2 random clusters
            ////using (var tran = Program.DBEngine.GetTransaction())
            ////{
            ////    tran.ValuesLazyLoadingIsOn = false; //to read key already with value

            ////    var res = tran.VectorsClusteringKMeans(tblKNNITLogos, 2);

            ////    foreach (var el in res)
            ////    {
            ////        foreach (var doc in el.Value)
            ////        {
            ////            Console.WriteLine($"CLUSTER: {el.Key}");
            ////            var rowDoc = tran.Select<byte[], string>(tblDocsITLogos, 2.ToIndex(doc));
            ////            var dbCompany = JsonSerializer.Deserialize<DBLogotype>(rowDoc.Value);
            ////            Console.WriteLine($"Company: {dbCompany.Logotype.Company}");
            ////            Console.WriteLine($"\tDescription: {dbCompany.Logotype.LogoDescription}");

            ////        }

            ////    }
            ////}//eo using


            ////----------SECOND APPROACH - not possible anymore to split complete table, security reasons, use other overloads of VectorsClusteringKMeans


            ////-from all items in tblKNNITLogos we try to create clusters around specifed documents
            //using (var tran = Program.DBEngine.GetTransaction())
            //{
            //    tran.ValuesLazyLoadingIsOn = false; //to read key already with value

            //    List<byte[]> twoClusters=new List<byte[]>();
            //    foreach(var row in tran.SelectForwardStartsWith<byte[], string>(tblDocsITLogos, 2.ToIndex()))
            //    {
            //        var dbCompany = JsonSerializer.Deserialize<DBLogotype>(row.Value);
            //        //Console.WriteLine($"{row.Key.Substring(1).To_Int32_BigEndian()} Company: {dbCompany.Logotype.Company}");

            //        if (dbCompany.Logotype.Company == "MICROSOFT")
            //            twoClusters.Add(row.Key.Substring(1));
            //        if (dbCompany.Logotype.Company == "META")
            //            twoClusters.Add(row.Key.Substring(1));
            //    }


            //    var res = tran.VectorsClusteringKMeans(tblKNNITLogos, 0, externalDocumentIDsAsCentroids: twoClusters);

            //    foreach (var el in res)
            //    {
            //        foreach (var doc in el.Value)
            //        {
            //            Console.WriteLine($"CLUSTER: {el.Key}");
            //            var rowDoc = tran.Select<byte[], string>(tblDocsITLogos, 2.ToIndex(doc));
            //            var dbCompany = JsonSerializer.Deserialize<DBLogotype>(rowDoc.Value);
            //            Console.WriteLine($"Company: {dbCompany.Logotype.Company}");
            //            Console.WriteLine($"\tDescription: {dbCompany.Logotype.LogoDescription}");

            //        }

            //    }
            //}//eo using


        }//eof


        public static void KMeansTest_Furniture()
        {

            //----------FIRST APPROACH

            ////-from all items in tblKNNFurniture we try to create 2 random clusters
            //using (var tran = Program.DBEngine.GetTransaction())
            //{
            //    tran.ValuesLazyLoadingIsOn = false; //to read key already with value

            //    var res = tran.VectorsClusteringKMeans(tblKNNFurniture, 10);

            //    foreach (var el in res)
            //    {
            //        Console.WriteLine($"CLUSTER: {el.Key}");

            //        foreach (var doc in el.Value)
            //        {                   
            //            var rowDoc = tran.Select<byte[], string>(tblDocsFurniture, 2.ToIndex(doc));
            //            var dbFurniture = JsonSerializer.Deserialize<FurnitureItem>(rowDoc.Value);
            //            Console.WriteLine($"\tCluster: {dbFurniture.Cluster}; name: {dbFurniture.Name}");                        
            //            //Console.WriteLine($"\tDescription: {dbFurniture.Description}");

            //        }

            //    }
            //}//eo using


            ////----------SECOND APPROACH

            //var furnitureLst = JsonSerializer.Deserialize<List<FurnitureV1>>(File.ReadAllText(@"..\..\..\TextCorpus\FurnitureV1withEmbeddings.json"));

            ////-from all items in tblKNNFurniture we try to create clusters around specifed documents
            //using (var tran = Program.DBEngine.GetTransaction())
            //{
            //    tran.ValuesLazyLoadingIsOn = false; //to read key already with value

            //    List<byte[]> Clusters = new List<byte[]>();

            //    string clusterNmae = "";

            //    //List<byte[]> twoClusters = new List<byte[]>();
            //    foreach (var row in tran.SelectForwardStartsWith<byte[], string>(tblDocsFurniture, 2.ToIndex()))
            //    {
            //        var dbFurniture = JsonSerializer.Deserialize<FurnitureItem>(row.Value);

            //        if (!dbFurniture.Cluster.Equals(clusterNmae))
            //        {
            //            Clusters.Add(row.Key.Substring(1));
            //            clusterNmae = dbFurniture.Cluster;
            //        }                  
            //    }


            //    var res = tran.VectorsClusteringKMeans(tblKNNFurniture, 0, externalDocumentIDsAsCentroids: Clusters);

            //    foreach (var el in res)
            //    {
            //        Console.WriteLine($"CLUSTER: {el.Key}");
            //        foreach (var doc in el.Value)
            //        {
            //            var rowDoc = tran.Select<byte[], string>(tblDocsFurniture, 2.ToIndex(doc));
            //            var dbFurniture = JsonSerializer.Deserialize<FurnitureItem>(rowDoc.Value);
            //            Console.WriteLine($"\tCluster: {dbFurniture.Cluster}; name: {dbFurniture.Name}");
            //            //Console.WriteLine($"\tDescription: {dbFurniture.Description}");

            //        }

            //    }
            //}//eo using


            //---------THIRD APPROACH KMean Finding cluster

            var furnitureLst = JsonSerializer.Deserialize<List<FurnitureV1>>(File.ReadAllText(@"..\..\..\TextCorpus\FurnitureV1withEmbeddings.json"));

            //-from all items in tblKNNFurniture we try to create clusters around specifed documents
            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.ValuesLazyLoadingIsOn = false; //to read key already with value

                Dictionary<int, double[]> Clusters = new Dictionary<int, double[]>();

                Dictionary<int, double[]> ItemsToCheck = new Dictionary<int, double[]>();

                string clusterNmae = "";

                //List<byte[]> twoClusters = new List<byte[]>();
                foreach (var row in tran.SelectForwardStartsWith<byte[], string>(tblDocsFurniture, 2.ToIndex()))
                {
                    var dbFurniture = JsonSerializer.Deserialize<FurnitureItem>(row.Value);

                    if (!dbFurniture.Cluster.Equals(clusterNmae))
                    {
                        Clusters.Add(row.Key.Substring(1).To_Int32_BigEndian(), dbFurniture.Embedding);
                        clusterNmae = dbFurniture.Cluster;
                    }

                    ItemsToCheck.Add(row.Key.Substring(1).To_Int32_BigEndian(), dbFurniture.Embedding);
                }


                //var res = tran.VectorsClusteringKMeans(clusterPrototypes: Clusters.Select(r => r.Value).ToList(), itemsToBeClustered: ItemsToCheck.Select(r => r.Value).ToList());

                //foreach (var el in res)
                //{
                //    Console.WriteLine($"CLUSTER: {el.Key}");
                //    foreach (var doc in el.Value)
                //    {
                        
                //        int index = doc + 1;
                //        var rowDoc = tran.Select<byte[], string>(tblDocsFurniture, 2.ToIndex(index));
                //        var dbFurniture = JsonSerializer.Deserialize<FurnitureItem>(rowDoc.Value);
                //        Console.WriteLine($"\tCluster: {dbFurniture.Cluster}; name: {dbFurniture.Name}");
                //        //Console.WriteLine($"\tDescription: {dbFurniture.Description}");

                //    }

                //}
            }//eo using


        }//eof



        // Root myDeserializedClass = JsonConvert.DeserializeObject<List<Root>>(myJsonResponse);
        public class FurnitureItem
        {
            public string Name { get; set; }
            public string Description { get; set; }

            public double[] Embedding { get; set; }

            public string Cluster { get; set; }
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
            //var furnitureLst = JsonSerializer.Deserialize<List<FurnitureV1>>(File.ReadAllText(@"..\..\..\TextCorpus\FurnitureV1withLocalEmbeddings.json"));
           
            using (var tran = Program.DBEngine.GetTransaction())
            {               
                //Flattering cluster prototypes and itemsTobeClustered
                Dictionary<int, (string ClusterName, FurnitureItem Furniture)> clusterPrototypes = new Dictionary<int, (string clusterName, FurnitureItem)>();
               // Dictionary<int, (string ClusterName, FurnitureItem Furniture)> itemsToBeClustered = new Dictionary<int, (string clusterName, FurnitureItem)>();
                List<(string ClusterName, FurnitureItem Furniture)> itemsToBeClustered = new List<(string clusterName, FurnitureItem)>();

                //Let's take first element from each Cluster as a Cluster prototype and the rest of elements will have to find to which cluster they belong to
                int i = 0;
                int j = 0;
                foreach (var cluster in furnitureLst)
                {
                    var first = cluster.Items.First();                    
                    clusterPrototypes.Add(i,(cluster.Cluster,first));
                    //Console.WriteLine($"Cluster: {i} - {cluster.Cluster}; Name: {first.Name}; Desc: {first.Description}");

                    foreach (var item in cluster.Items.Skip(1)) //skipping first value from the cluster and adding to itemsToBeClustered
                    {
                        //itemsToBeClustered.Add(j, (cluster.Cluster, item));
                        item.Cluster = cluster.Cluster;
                        itemsToBeClustered.Add((cluster.Cluster, item));
                        j++;
                    }
                   
                    i++;
                }

                //- shuffling of itemsToBeClustered, for fun of course
                List<int> hs=new List<int>();
                Random rnd = new Random();
                int ki = 0;
                foreach (var item in itemsToBeClustered)
                {
                    hs.Add(ki);
                    ki++;
                }
                List<(string ClusterName, FurnitureItem Furniture)> itemsToBeClustered2 =new List<(string ClusterName, FurnitureItem Furniture)>();
                foreach (var item in itemsToBeClustered)
                {
                    if (hs.Count == 0)
                        break;
                    var pos = rnd.Next(hs.Count);
                    itemsToBeClustered2.Add(itemsToBeClustered[hs[pos]]);
                    hs.RemoveAt(pos);
                }
                //- end of shuffle

                itemsToBeClustered = itemsToBeClustered2;

                //- shuffle variation
                    //Random random = new Random();
                    //int n = itemsToBeClustered.Count;
                    //while (n > 1)
                    //{
                    //    n--;
                    //    int k = random.Next(n + 1);
                    //    var value = itemsToBeClustered[k];
                    //    itemsToBeClustered[k] = itemsToBeClustered[n];
                    //    itemsToBeClustered[n] = value;
                    //}
                //- end of shuffle



                //foreach (var item in itemsToBeClustered2)
                //{
                //    Console.WriteLine($"Cluster: {i} - {item.ClusterName}; Name: {item.Furniture.Name}; ");// Desc: {item.Furniture.Description}");
                //}


                //-supplying two Lists of embeddings, first are prototypes that represent different clusters and second are items to be sparsed between all prototypes clusters
                //var res = tran.VectorsClusteringKMeans(
                //    clusterPrototypes.Values.Select(r=>r.Item2.Embedding).ToList(),
                //    //shuffled itemsToBeClustered
                //    itemsToBeClustered.Select(r => r.Item2.Embedding).ToList()
                //    );


                //i = 0;
                ////resulting output               
                //foreach (var el in res)
                //{
                //    Console.WriteLine($"CLUSTER: -------------{clusterPrototypes[i].ClusterName}-------------");
                //    foreach (var item in el.Value)
                //    {
                //        //Console.WriteLine($"\t OriginalCluster: {itemsToBeClustered[j].ClusterName}; Name: {itemsToBeClustered[j].Furniture.Name}");
                //        Console.WriteLine($"\t OriginalCluster: {itemsToBeClustered[item].ClusterName}; Name: {itemsToBeClustered[item].Furniture.Name}");                       
                //    }
                //    i++;
                //}

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

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static async Task GetFunrnitureV2Embeddings()
        {
            var furnitureLst = JsonSerializer.Deserialize<List<FurnitureV1>>(File.ReadAllText(@"..\..\..\TextCorpus\FurnitureV1.json"));

            foreach (var cluster in furnitureLst)
            {
                foreach (var clusterItem in cluster.Items)
                {
                    var emb = await OpenAI.GetLocalEmbedding(cluster.Cluster + " " + clusterItem.Name + " " + clusterItem.Description).ConfigureAwait(false);
                    if (emb != null)
                    {
                        //clusterItem.Embedding = emb.EmbeddingAnswer.ToArray();
                        clusterItem.Embedding = emb.embeddings[0];
                    }
                    else
                    {

                    }
                }
            }

            File.WriteAllText(@"..\..\..\TextCorpus\FurnitureV1withLocalEmbeddings.json", JsonSerializer.Serialize(furnitureLst));
        }

        

    }//eoc
}//eon
