using System;
using System.Text.Json.Nodes;
using DBreeze;
using DBreeze.Utils;
using static TesterNet6.OpenAI;
using System.Text.Json;
using TesterNet6.TextCorpus;
using DBreeze.Tries;


namespace TesterNet6 
{
    internal class Program
    {

        public static DBreezeEngine DBEngine = null;

        public static string PathToOpenAIKey = @"H:\OaiApiKey.txt";
        public static string PathToDatabase = @"D:\Temp\DBVector";

      
        static async Task Main(string[] args)
        {
            InitDB();
            OpenAI.Init(PathToOpenAIKey);

            //Examples
            //await ITGiantLogotypes.Example();

            Clusterization.KMeansTest();



            //Technical helpers
            //Biser_Objectify();


            await Task.Run(() =>
            {
                Console.WriteLine("Press any key");
                Console.ReadLine();
            });
            
        }


        /// <summary>
        /// 
        /// </summary>
        static void InitDB()
        {
            string DBPath = PathToDatabase;
            DBreezeConfiguration conf = new DBreezeConfiguration()
            {
                DBreezeDataFolderName = DBPath,
                Storage = DBreezeConfiguration.eStorage.DISK,
            };
            conf.AlternativeTablesLocations.Add("mem_*", String.Empty);
            DBEngine = new DBreezeEngine(conf);

            DBreeze.Utils.CustomSerializator.ByteArraySerializator = TesterNet6.ProtobufExtension.SerializeProtobuf;
            DBreeze.Utils.CustomSerializator.ByteArrayDeSerializator = TesterNet6.ProtobufExtension.DeserializeProtobuf;
        }

        static void Biser_Objectify()
        {
      //             var resbof = BiserObjectify.Generator.Run(typeof(DBreeze.HNSW.NodeInDb), true,
      //      @"C:\Users\Secure\Documents\VSProjects\tests\HNSW\DB\", forBiserBinary: true, forBiserJson: false, null);

      //      resbof = BiserObjectify.Generator.Run(typeof(DBreeze.HNSW.ItemInDbFloatArray), true,
      // @"C:\Users\Secure\Documents\VSProjects\tests\HNSW\DB\", forBiserBinary: true, forBiserJson: false, null);

      //      resbof = BiserObjectify.Generator.Run(typeof(DBreeze.HNSW.VectorStat), true,
      //@"C:\Users\Secure\Documents\VSProjects\tests\HNSW\DB\", forBiserBinary: true, forBiserJson: false, null);

        }
    }
}