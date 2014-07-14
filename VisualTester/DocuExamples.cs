using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DBreeze;
using DBreeze.Utils;
using DBreeze.DataTypes;

namespace VisualTester
{
    public class DocuExamples:IDisposable
    {
        DBreezeEngine engine = null;

        public DocuExamples(string dbreezeFolder)
        {
            engine = new DBreezeEngine(dbreezeFolder);
        }

        public void Dispose()
        {
            if (engine != null)
                engine.Dispose();
        }


        public void Example_GettingStartedWithNestedTables()
        {
            engine.Scheme.DeleteTable("t1");

            using (var tran = engine.GetTransaction())
            {
                try
                {

                    //////////tran.Insert<byte[], string>("t1", new byte[] { 1 }, "h1");
                    //////////tran.Insert<byte[], string>("t1", new byte[] { 1,1 }, "h1");

                    ////////tran.Insert<int, string>("t1", 1, "h1");
                    ////////tran.Insert<int, string>("t1", 2, "h1");
                   

                    ////////tran.RemoveAllKeys("t1", false);

                    ////////tran.Insert<int, string>("t1", 1, "h1");
                    //////////tran.RemoveKey<int>("t1", 1);
                    ////////tran.Commit();

                    ////////tran.Select<int, string>("t1", 1).PrintOut();



                    var horizontal = tran
                        .InsertTable<int>("t1", 4, 0);

                    horizontal.Insert<int, string>(1, "Hi1");

                    horizontal
                        .GetTable<int>(2, 0)
                        .Insert(1, "Xi1")
                        .Insert(2, "Xi2");

                    horizontal
                        .GetTable<int>(2, 1)
                        .Insert(7, "Piar7")
                        .Insert(8, "Piar8");

                    horizontal.Insert<int, string>(3, "Hi1");

                    //horizontal
                    //   .GetTable<int>(2, 1)
                    //   .Insert(7, "Piar99")
                    //   .Insert(8, "Piar8");

                    //horizontal
                    //    .GetTable<int>(2, 1)
                    //    .RemoveAllKeys();

                    //horizontal
                    //    .GetTable<int>(2, 1)
                    //    .RemoveKey(7);

                    //horizontal
                    //  .GetTable<int>(2, 1)
                    //  .Insert(7, "Piar999");

                    //tran.SelectTable<int>("t1", 4, 0)
                    //   .GetTable<int>(2, 1)
                    //   .Select<int, string>(7)
                    //   .PrintOut();


                    tran.Commit();



                    tran.SelectTable<int>("t1", 4, 0)
                        .GetTable<int>(2, 1)
                        .Select<int, string>(7)
                        .PrintOut();


                    //var nt =  tran
                    //    .InsertTable<int>("t1", 4, 0)                        
                    //    .GetTable<int>(2,0);

                    //var nt1 = tran
                    //    .InsertTable<int>("t1", 4, 0)
                    //    .GetTable<int>(2, 1);

                    //nt
                    //    .Insert<int, string>(1,"Xi1")
                    //    .Insert<int,s

                    
                    //tran.Insert<int, string>("t1", 1, "hello");
                    //tran.Insert<int, byte[]>("t1", 2, new byte[] { 1, 2, 3 });
                    //tran.Insert<int, decimal>("t1", 3, 324.34M);
                    //tran.InsertTable<int>("t1", 4, 0);
                    //tran.InsertPart<int, int>("t1", 4, 587, 64);

                    //tran.SelectTable<int>("t1", 4, 0);

                    //tran
                    //    .InsertTable<int>("t1", 4, 0)
                    //    .Insert<int, string>(1, "Hi1")
                    //    .Insert<int, string>(2, "Hi2")
                    //    .Insert<int, string>(3, "Hi3");

                    //tran.Commit();


                    //foreach (var row in tran
                    //                    .SelectTable<int>("t1", 4, 0)
                    //                    .SelectForward<int, string>()
                    //    )
                    //{
                    //    row.PrintOut();
                    //}



                    //tran.SelectTable<int>("t1", 4, 0)
                    //    .Select<int, string>(1)
                    //    .PrintOut();

                    //var row = tran.SelectTable<int>("t1", 4, 0)
                    //    .Select<int, string>(1);

                    //row.PrintOut();

                    //row.GetTable().Select<int, string>(2).PrintOut();
                    //row.GetTable().Select<int, string>(3).PrintOut();

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Example_InsertingData()
        {
            engine.Scheme.DeleteTable("t1");

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    tran.Insert<int, int>("t1", 10, 2);
                    tran.Commit();

                    var row = tran.Select<int, int>("t1", 10);

                    byte[] btRes = null;
                    int res=0;
                    int key=0;
                    if (row.Exists)
                    {
                        key = row.Key;
                        res = row.Value;
                        btRes = row.GetValuePart(12);
                        btRes = row.GetValuePart(12, 1);
                    }                   
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        public void Example_FetchingRange()
        {
            engine.Scheme.DeleteTable("t1");

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    DateTime dt = DateTime.Now;

                    DBreeze.Diagnostic.SpeedStatistic.StartCounter("INSERT");

                    for (int i = 0; i < 1000000; i++)
                    {
                        tran.Insert<DateTime, byte?>("t1", dt, null);

                        dt = dt.AddSeconds(7);
                    }

                    tran.Commit();

                    DBreeze.Diagnostic.SpeedStatistic.StopCounter("INSERT");

                    DBreeze.Diagnostic.SpeedStatistic.PrintOut(true);

                    DBreeze.Diagnostic.SpeedStatistic.StartCounter("FETCH");

                    foreach (var row in tran.SelectForward<DateTime, byte?>("t1"))
                    {
                        //Console.WriteLine("K: {0}; V: {1}", row.Key.ToString("dd.MM.yyyy HH:mm:ss"), (row.Value == null) ? "NULL" : row.Value.ToString());
                    }

                    DBreeze.Diagnostic.SpeedStatistic.StopCounter("FETCH");

                    DBreeze.Diagnostic.SpeedStatistic.PrintOut(true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        public void Example_FetchingRange1()
        {
            engine.Scheme.DeleteTable("t1");

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    
                    DateTime dt = DateTime.Now;

                    DBreeze.Diagnostic.SpeedStatistic.StartCounter("INSERT");

                    for (int i = 0; i < 1000000; i++)
                    {
                        tran.Insert<DateTime, string>("t1", dt, "Привет, We are überall");  //byte[8] datetime + byte[29] text

                        dt = dt.AddSeconds(7);
                    }
                    
                    tran.Commit();

                    DBreeze.Diagnostic.SpeedStatistic.StopCounter("INSERT");    

                    DBreeze.Diagnostic.SpeedStatistic.PrintOut(true);       //10 s

                    DBreeze.Diagnostic.SpeedStatistic.StartCounter("FETCH");

                    string text=String.Empty;

                    

                    foreach (var row in tran.SelectForward<DateTime, string>("t1"))
                    {
                        text = row.Value;
                        //Console.WriteLine("K: {0}; V: {1}", row.Key.ToString("dd.MM.yyyy HH:mm:ss"), (row.Value == null) ? "NULL" : row.Value.ToString());
                    }

                    DBreeze.Diagnostic.SpeedStatistic.StopCounter("FETCH");

                    DBreeze.Diagnostic.SpeedStatistic.PrintOut(true);   //9 s, FS:50 MB
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        public class Article
        {
            public Article()
            {
                Id = 0;
                Name = String.Empty;
                Price = 0f;
            }

            public uint Id { get; set; }
            public string Name { get; set; }
            public float Price { get; set; }
        }

        public void Example_InsertingObject()
        {
            engine.Scheme.DeleteTable("Articles");

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    tran.SynchronizeTables("Articles");

                    uint identity = 0;

                    var row = tran.Max<uint, byte[]>("Articles");

                    if (row.Exists)
                        identity = row.Key;

                    identity++;

                    Article art = new Article()
                    {
                        Id = identity,
                        Name = "PC"
                    };
                    tran.Insert<uint, DbMJSON<Article>>("Articles", identity, art);

                    tran.Commit();

                    foreach (var row1 in tran.SelectForward<uint, DbMJSON<Article>>("Articles").Take(10))
                    {
                        //row1.Value.Get
                        //row1.Value.SerializedObject
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        public void Example_NonUniqueKey()
        {
            engine.Scheme.DeleteTable("Articles");

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    uint id = 0;


                    Article art = new Article()
                    {
                        Name = "Notebook",
                        Price = 100.0f
                    };

                    id++;
                    tran.Insert<uint, DbMJSON<Article>>("Articles", id, art);

                    byte[] idAsByte = id.To_4_bytes_array_BigEndian();
                    byte[] priceKey = art.Price.To_4_bytes_array_BigEndian().Concat(idAsByte);
                    Console.WriteLine("{0}; Id: {1}; IdByte[]: {2}; btPriceKey: {3}", art.Name, id, idAsByte.ToBytesString(""), priceKey.ToBytesString(""));
                    tran.Insert<byte[], byte[]>("Prices", priceKey, null);

                    art = new Article()
                    {
                        Name = "Keyboard",
                        Price = 10.0f
                    };

                    id++;
                    tran.Insert<uint, DbMJSON<Article>>("Articles", id, art);

                    idAsByte = id.To_4_bytes_array_BigEndian();
                    priceKey = art.Price.To_4_bytes_array_BigEndian().Concat(idAsByte);
                    Console.WriteLine("{0}; Id: {1}; IdByte[]: {2}; btPriceKey: {3}", art.Name, id, idAsByte.ToBytesString(""), priceKey.ToBytesString(""));
                    tran.Insert<byte[], byte[]>("Prices", priceKey, null);


                    art = new Article()
                    {
                        Name = "Mouse",
                        Price = 10.0f
                    };

                    id++;
                    tran.Insert<uint, DbMJSON<Article>>("Articles", id, art);

                    idAsByte = id.To_4_bytes_array_BigEndian();
                    priceKey = art.Price.To_4_bytes_array_BigEndian().Concat(idAsByte);
                    Console.WriteLine("{0}; Id: {1}; IdByte[]: {2}; btPriceKey: {3}", art.Name, id, idAsByte.ToBytesString(""), priceKey.ToBytesString(""));
                    tran.Insert<byte[], byte[]>("Prices", priceKey, null);

                    art = new Article()
                    {
                        Name = "Monitor",
                        Price = 200.0f
                    };

                    id++;
                    tran.Insert<uint, DbMJSON<Article>>("Articles", id, art);

                    idAsByte = id.To_4_bytes_array_BigEndian();
                    priceKey = art.Price.To_4_bytes_array_BigEndian().Concat(idAsByte);
                    Console.WriteLine("{0}; Id: {1}; IdByte[]: {2}; btPriceKey: {3}", art.Name, id, idAsByte.ToBytesString(""), priceKey.ToBytesString(""));
                    tran.Insert<byte[], byte[]>("Prices", priceKey, null);

                    art = new Article()
                    {
                        Name = "MousePad",
                        Price = 3.0f
                    };

                    id++;
                    tran.Insert<uint, DbMJSON<Article>>("Articles", id, art);

                    idAsByte = id.To_4_bytes_array_BigEndian();
                    priceKey = art.Price.To_4_bytes_array_BigEndian().Concat(idAsByte);
                    Console.WriteLine("{0}; Id: {1}; IdByte[]: {2}; btPriceKey: {3}", art.Name, id, idAsByte.ToBytesString(""), priceKey.ToBytesString(""));
                    tran.Insert<byte[], byte[]>("Prices", priceKey, null);

                    tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }

            Console.WriteLine("***********************************************");

            //Fetching data >=
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    //We are interested here in Articles with the cost >= 10

                    float price = 10f;
                    uint fakeId = 0;

                    byte[] searchKey = price.To_4_bytes_array_BigEndian().Concat(fakeId.To_4_bytes_array_BigEndian());

                    Article art = null;

                    foreach (var row in tran.SelectForwardStartFrom<byte[], byte[]>("Prices", searchKey, true))
                    {
                        Console.WriteLine("Found key: {0};", row.Key.ToBytesString(""));

                        var artRow = tran.Select<uint, DbMJSON<Article>>("Articles", row.Key.Substring(4, 4).To_UInt32_BigEndian());

                        if (artRow.Exists)
                        {
                            art = artRow.Value.Get;
                            Console.WriteLine("Articel: {0}; Price: {1}", art.Name, art.Price);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }

            Console.WriteLine("***********************************************");

            //Fetching data >
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    //We are interested here in Articles with the cost > 10

                    float price = 10f;
                    uint fakeId = UInt32.MaxValue;

                    byte[] searchKey = price.To_4_bytes_array_BigEndian().Concat(fakeId.To_4_bytes_array_BigEndian());

                    Article art = null;

                    foreach (var row in tran.SelectForwardStartFrom<byte[], byte[]>("Prices", searchKey, true))
                    {
                        Console.WriteLine("Found key: {0};", row.Key.ToBytesString(""));

                        var artRow = tran.Select<uint, DbMJSON<Article>>("Articles", row.Key.Substring(4, 4).To_UInt32_BigEndian());

                        if (artRow.Exists)
                        {
                            art = artRow.Value.Get;
                            Console.WriteLine("Articel: {0}; Price: {1}", art.Name, art.Price);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }


        }


    }
}
