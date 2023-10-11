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
    internal class Clusterization
    {
        static string tblKNNITLogos = "KNNITLogos"; //Vector Table for ITLogos
        static string tblDocsITLogos = "DocsITLogos"; //Docs ItLogos

        public static void KMeansTest()
        {
            using (var tran = Program.DBEngine.GetTransaction())
            {
                tran.ValuesLazyLoadingIsOn = false; //to read key already with value

                var res = tran.VectorsClusterizationKMeans(tblKNNITLogos, 2);

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
            }

        }
    }
}
