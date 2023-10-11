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


            //-from all items in tblKNNITLogos we try to create clusters around 
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



    }//eoc
}//eon
