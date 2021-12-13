using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using DBreeze.Utils;
using DBreeze.DataStructures;

using System.IO;
using DBreeze;
using DBreeze.Diagnostic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;

namespace VisualTester
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }


        FastTests _ft = new FastTests();



        private void btTest1_Click(object sender, RoutedEventArgs e)
        {
            _ft.RUN_LTrie_MainTests();
        }

        private void btTest2_Click(object sender, RoutedEventArgs e)
        {
            _ft.RUN_FETCH();
        }

        Dictionary<long, double> _dC = new Dictionary<long, double>();
        Dictionary<double, byte[]> _d1C = new Dictionary<double, byte[]>();

        Dictionary<float, byte[]> _fC = new Dictionary<float, byte[]>();


        private byte[] GetBytes(double d)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(ms))
                {
                    writer.Write(d);
                    writer.Flush();
                    writer.Close();
                }

                return ms.ToArray();
            }
        }

        private void PrintOutDoubles(Dictionary<double, byte[]> xd)
        {
            foreach (var el in xd.OrderBy(r => r.Key))
            {
                Console.WriteLine("D: {0}; B: {1}", el.Key, el.Value.ToBytesString(""));
            }
        }



        //public class ByteListComparer : IComparer<IList<byte>>
        //{           
        //    public int Compare(IList<byte> x, IList<byte> y)
        //    {
        //        int result;
        //        int min = Math.Min(x.Count, y.Count);
        //        for (int index = 0; index < min; index++)
        //        {
        //            result = x[index].CompareTo(y[index]);
        //            if (result != 0)
        //                return result;
        //        }
        //        return x.Count.CompareTo(y.Count);
        //    }
        //}




        //ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();
        //MultiKeyDictionary<(int cid, int wid, float aid), string> mkd41 = new MultiKeyDictionary<(int, int, float), string>();

        //private void ReadFrom_MKD((int cid, int wid, float aid) newKey, string newValue)
        //{
        //    _sync.EnterReadLock();
        //    try
        //    {
        //        if (!mkd41.Contains(newKey)) //recheck
        //        {
        //            mkd41.Add(newKey, newValue);
        //        }

        //    }
        //    finally
        //    {
        //        _sync.ExitReadLock();
        //    }
        //}

        //private void WriteInto_MKD((int cid, int wid, float aid) newKey, string newValue)
        //{
        //    _sync.EnterWriteLock();
        //    try
        //    {
        //        if (!mkd41.Contains(newKey)) //recheck
        //        {
        //            mkd41.Add(newKey, newValue);
        //        }

        //    }
        //    finally
        //    {
        //        _sync.ExitWriteLock();
        //    }
        //}

        //private void WriteInto_MKD_IfKeyNotFound((int cid, int wid, float aid) newKey, string newValue)
        //{
        //    _sync.EnterUpgradeableReadLock();
        //    try
        //    {
        //        if (!mkd41.Contains(newKey)) //emulating write ONLY in case when key was not found
        //        {
        //            _sync.EnterWriteLock();
        //            try
        //            {
        //                if (!mkd41.Contains(newKey)) //recheck
        //                {
        //                    mkd41.Add(newKey, newValue);
        //                }

        //            }
        //            finally
        //            {
        //                _sync.ExitWriteLock();
        //            }
        //        }
        //    }            
        //    finally
        //    {
        //        _sync.ExitUpgradeableReadLock();
        //    }
        //}


        [ProtoBuf.ProtoContract]
        public class Human
        {

            public Human()
            {

            }

            [ProtoBuf.ProtoMember(1, IsRequired = true)]
            public int Age { get; set; } = 0;

        }


        private void btTest10_Click(object sender, RoutedEventArgs e)
        {

            return;
            //DBreezeEngine dbe = new DBreezeEngine(new DBreezeConfiguration {  
            //    DBreezeDataFolderName = @"C:\Users\Secure\Documents\VSProjects\tests\1\dbtmp", 
            //    NotifyAhead_WhenWriteTablePossibleDeadlock = false } );

            DBreezeEngine dbe = new DBreezeEngine(@"D:\Temp\1\dbtmp");


            using (var tran = dbe.GetTransaction())
            {
                //tran.SynchronizeTables("t*");
              
                //for (int i = 0; i < 20; i++)
                //{
                //    tran.Insert<int, int>("t1", i, i);
                //    tran.Insert<int, int>("t2", ++i, i);
                //    tran.Insert<int, int>("t3", ++i, i);
                //}
                //// Manual values for duplicate keys between tables
                //tran.Insert<int, int>("t1", 5, 50);
                //tran.Insert<int, int>("t2", 8, 80);
                //tran.Insert<int, int>("t3", 15, 150);
                //tran.Commit();

                //    //t.SynchronizeTables("t1", "t2");
                //    //t.SynchronizeTables("t*");
                //    //t.SynchronizeTables("t1");
                //    //t.SynchronizeTables("t1");

                //    //t.RandomKeySorter.Insert("t1", 1, 1);

                //    t.Insert("t1", 1, 1);
                //    //t.SynchronizeTables("t1", "t2");
                //    t.Insert("t2", 1, 1);
                //    t.Commit();
                HashSet<string> tables = new HashSet<string>() { "t1", "t4", "t2", "t3" };

                foreach (var el in tran.Multi_SelectForwardFromTo<int, int>(tables, int.MinValue, true, int.MaxValue, true))
                {
                    Console.WriteLine($"tbl: {el.TableName}: Key: {el.Key}; Value: {el.Value}");
                }

                Console.WriteLine("-------------------------------");

                foreach (var el in tran.Multi_SelectBackwardFromTo<int, int>(tables, int.MaxValue, true, int.MinValue, true))
                {
                    Console.WriteLine($"tbl: {el.TableName}: Key: {el.Key}; Value: {el.Value}");
                }
            }

            return;

            try
            {
                using (var t = dbe.GetTransaction())
                {
                    foreach(var row in t.SelectBackwardFromTo<int,int>("t1",7,true,3,true,2))
                    {
                        Console.WriteLine(row.Key);
                        
                    }
                    Console.WriteLine("-------");
                    foreach (var row in t.SelectForwardFromTo<int, int>("t1", 5, true, 9, true, 2))
                    {
                        Console.WriteLine(row.Key);

                    }
                }

                return;

                using (var t = dbe.GetTransaction())
                {

                    for (int i = 0; i < 10; i++)
                    {
                        t.Insert<int, int>("t1", i, i);
                    }

                    //t.SynchronizeTables("t1", "t2");
                    //t.SynchronizeTables("t*");
                    //t.SynchronizeTables("t1");
                    //t.SynchronizeTables("t1");

                    //t.RandomKeySorter.Insert("t1", 1, 1);

                    //t.Insert("t1", 1, 1);
                    //t.SynchronizeTables("t1", "t2");
                    //t.Insert("t1", 1, 1);


                    t.Commit();
                }
            }
            catch (Exception ex)
            {

            }
           

            return;
            //Dictionary<Tuple<int,int>, string> fd9 = new Dictionary<Tuple<int, int>, string>();
            //fd9.Add(new Tuple<int, int>(15,15), "12");
            //fd9.Add(new Tuple<int, int>(16,17), "14");

            //fd9.TryGetValue(new Tuple<int, int>(16, 17), out var tzz91);

            //var trzuz1 = fd9.SerializeProtobuf();
            //var trzfd = (Dictionary<Tuple<int, int>, string>)ProtobufHelper.DeserializeProtobuf(trzuz1, fd9.GetType());

            //Dictionary<(int cid, decimal wid, int aid, float, int, int, string, uint, double, int), string> fd7 = new Dictionary<(int, decimal, int, float, int, int, string, uint, double, int), string>();

            //fd7.Add((2, 1m, 3, 45.6f, 34, 54, "dsf", 45, 34.7, 23), "a15");
            //fd7.Add((2, 1m, 3, 45.6f, 34, 54, "dsf", 45, 35.7, 23), "a16");

            //fd7.TryGetValue((2, 1m, 3, 45.6f, 34, 54, "dsf", 45, 34.7, 23), out var tzz71);

            //var trzuz= fd7.SerializeProtobuf();

            //Dictionary<(int cid, decimal wid, int aid, float, int, int, string, uint, double, int), string> fd8 = new Dictionary<(int, decimal, int, float, int, int, string, uint, double, int), string>();
            //fd8=(Dictionary<(int cid, decimal wid, int aid, float, int, int, string, uint, double, int), string>)ProtobufHelper.DeserializeProtobuf(trzuz, fd8.GetType());
            //fd8[(2, 1m, 3, 45.6f, 34, 54, "dsf", 45, 34.7, 23)] = "a17";

            ////Dictionary< (int cid, long wid, int, int, int, int, int, int, int, int), int> fd6 = new Dictionary<(int cid, long wid, int, int, int, int, int, int, int, int), int>();
            ////fd6.Add((1, (12,12)), 1);
            ////fd6.Add((1, (12, 14)), 2);

            ////fd6.TryGetValue((1, (12, 14)), out var tzz61);


            //Dictionary<(int was,int las), int> fd5 = new Dictionary<(int,int), int>();
            //fd5.Add((1,1), 1);
            //fd5.Add((1,2), 2);

            //fd5.TryGetValue((1, 1), out var tzz51);
            //fd5.TryGetValue((1, 2), out var tzz52);

            //return;
            //Dictionary<byte[], int> fd = new Dictionary<byte[], int>();
            //var btKey = new byte[] { 1, 2, 3 };
            //fd.Add(btKey, 1);
            //fd.Add(new byte[] { 1, 2, 3, 4 }, 2);

            //fd.TryGetValue(btKey, out var tzz);

            //Dictionary<Human, int> fd1 = new Dictionary<Human, int>();
            //var hm = new Human { Age = 12 };
            //fd1.Add(hm, 1);
            //fd1.Add(new Human { Age=15 }, 2);

            //fd1.TryGetValue(hm, out var tzz1);
            //fd1.TryGetValue(new Human { Age = 15 }, out var tzz2);

            ////DBreeze.Utils.Hash.MurMurHash.MixedMurMurHash3_128()

            //MultiKeyDictionary<(int? cid, int wid, byte[] aid), string> mkd31 = new MultiKeyDictionary<(int?, int, byte[]), string>();
            //return;

            ////MultiKeyDictionary omkd = new MultiKeyDictionary();
            ////omkd.Add("fdf", 1, 2, 3);
            ////omkd.Add("fdf", 1, 2, 4);
            ////omkd.Add("fdf", 1, 3, 4);
            ////omkd.Add("fdf", 1, 3, 4);
            ////omkd.Add("fdf", 1, 3, 5);
            ////Console.WriteLine(omkd.Count);
            ////omkd.Remove(1, 3, 4);
            ////Console.WriteLine(omkd.Count);
            ////omkd.Remove(1, 3);
            ////Console.WriteLine(omkd.Count);

            ////return;
            MultiKeyDictionary.ByteArraySerializator = ProtobufHelper.SerializeProtobuf;
            MultiKeyDictionary.ByteArrayDeSerializator = ProtobufHelper.DeserializeProtobuf;

            //MultiKeyDictionary<(int cid, long wid,int,int,int,int,int,int,int,int), string> tmkd = new MultiKeyDictionary<(int, long, int, int, int, int, int, int, int, int), string>();
            //tmkd.Add((1, 1,2,3,4,5,6,7,8,9), "a1");
            //tmkd.Add((1, 2,2,3,4,5,6,7,8,9), "a3");
            //tmkd.Add((2, 1,2,3,4,5,6,7,8,9), "a4");
            //tmkd.Add((4, 1,2,3,4,5,6,7,8,9), null);
            //tmkd.Add((5, 1,2,3,4,5,6,7,8,9), "a5");

            //var bt54 = tmkd.Serialize();

            //MultiKeyDictionary<(int cid, long wid, int, int, int, int, int, int, int, int), string> tmkd1 = new MultiKeyDictionary<(int, long, int, int, int, int, int, int, int, int), string>();
            //tmkd1.Deserialize(bt54);

            //foreach (var el in tmkd1.GetAll())
            //{
            //   // Console.WriteLine(el.Item2);
            //}

            ////return;

            MultiKeyDictionary<(int was, (int, int), decimal), string> hzu = new MultiKeyDictionary<(int, (int, int), decimal), string>();
            hzu.Add((1, (2, 2),65m), "dfs1");
            hzu.Add((1, (2, 3),65m), "dfs2");
            hzu.Add((1, (2, 4),65m), "dfs3");
            hzu.Add((2, (2, 2),65m), "dfs4");
            hzu.Add((2, (2, 3),65m), "dfs5");
            hzu.Add((2, (2, 4),65m), "dfs6");
            hzu.Add((3, (2, 2),65m), "dfs7");
            hzu.Add((3, (2, 3),65m), "dfs8");
            hzu.Add((3, (2, 4),65m), "dfs9");

            hzu[(3, (2, 4), 65m)] = "dfs19";

            var tr243254 = hzu.Serialize();
            MultiKeyDictionary<(int was, (int, int), decimal), string> hzu1 = new MultiKeyDictionary<(int, (int, int), decimal), string>();
            hzu1.Deserialize(tr243254);

            hzu1.Remove(2);

            foreach (var el in hzu1.GetByKeyStart(3))
            {
                Console.WriteLine(el.Item1.was + "__" + el.Item2);
            }


            MultiKeyDictionary<(int cid, decimal wid, int aid, float, int, int, string, uint, double, int), string> bmkd = new MultiKeyDictionary<(int, decimal, int, float, int, int, string, uint, double, int), string>();

            bmkd.Add((1, 1m, 1, 45.6f, 34, 54, "fdf", 45, 34.7, 23), "a1");
            bmkd.Add((2, 1m, 3, 45.6f, 34, 54, "dsf", 45, 34.7, 23), "a15");




            var bttr = bmkd.Serialize();

            MultiKeyDictionary<(int cid, decimal wid, int aid, float, int, int, string, uint, double, int), string> bmkd1 = new MultiKeyDictionary<(int, decimal, int, float, int, int, string, uint, double, int), string>();
            bmkd1.Deserialize(bttr);

            foreach (var el in bmkd1.GetAll())
            {
                Console.WriteLine(el.Item2);
            }

            return;
            MultiKeyDictionary<(int cid, int wid, int aid), string> mkd = new MultiKeyDictionary<(int, int, int), string>();

            mkd.Add((1, 1, 1),"a1");
            mkd.Add((1, 2, 3),"a3");
            mkd.Add((1, 2, 4),"a4");
            mkd.Add((1, 2, 5),"a4");
            mkd.Add((1, 3, 9),"a9");
            mkd.Add((1, 3, 10),"a10");
            mkd.Add((2, 1, 7),"a7");
            mkd.Add((2, 2, 8), "a8");

            //var rr = mkd.TryGetValue((1, 16, 2), out var valll);
            //var rr1 = mkd.TryGetValue((1, 3, 9), out var valll1);

            //Console.WriteLine(mkd.Count);
            ////mkd.Remove((1,2,3));
            //mkd.Remove(1);
            //Console.WriteLine(mkd.Count);
            ////foreach (var el in mkd.GetByKeyStart((1,2)))
            ////{
            ////    Console.WriteLine(el.Item2);
            ////}



            foreach (var el in mkd.GetAll())
            {
                
                Console.WriteLine(el.Item1.aid);
            }

            var newmkd = mkd.CloneMultiKeyDictionary();


            //var tr = mkd.SerializeProtobuf();

            var bt = mkd.Serialize();

            MultiKeyDictionary<(int cid, int wid, int aid), string> mkd1 = new MultiKeyDictionary<(int, int, int), string>();
            mkd1.Deserialize(bt);


            return;
            this.btTest3_Click(null, null);

            using (var tran = xfre.GetTransaction())
            {
                var tsm = tran.TextSearch("tblText");

                tsm.ExternalDocumentIdStart = ((long)14).ToBytes();
                tsm.ExternalDocumentIdStop = ((long)9).ToBytes();

                //tsm.ExternalDocumentIdStart = ((long)993).ToBytes();
                //tsm.ExternalDocumentIdStop = ((long)990).ToBytes();

                //tsm.ExternalDocumentIdStart = ((long)990).ToBytes();
                //tsm.ExternalDocumentIdStop = ((long)993).ToBytes();

                //tsm.ExternalDocumentIdStart = ((long)1990).ToBytes();
                //tsm.ExternalDocumentIdStop = ((long)1993).ToBytes();
                //tsm.Descending = false;

                //tsm.ExternalDocumentIdStart = new byte[] { 1, 2, 5 };
                //tsm.ExternalDocumentIdStop = new byte[] { 1, 2, 4 };

                //tsm.Descending = false;

                foreach (var w in
                  tsm.BlockAnd("boy", "UIO")
                  .GetDocumentIDs())
                {
                    Console.WriteLine(w.To_Int64_BigEndian());
                }

                //var lst = new List<string> { "#PK_3", "#PK_6" };
                //foreach (var item in tsm.Block(null, string.Join(" ", lst), false).GetDocumentIDs())
                //{
                //    cl($"{item.Substring(0, 4).To_Int32_BigEndian()}-{item.Substring(4, 4).To_Int32_BigEndian()}");
                //}
            }
            return;

            using (var tran = xfre.GetTransaction())
            {
                //tran.TextInsert("tblText", new byte[] { 1, 2, 3 }, "boy", "UIO");
                //tran.TextInsert("tblText", new byte[] { 1, 2, 4 }, "boy", "UIO");
                //tran.TextInsert("tblText", new byte[] { 1, 2, 5 }, "boy", "UIO");
                //tran.TextInsert("tblText", new byte[] { 1, 2, 6 }, "boy", "UIO");


                for (int i = 8; i < 994; i++)
                {
                    tran.TextInsert("tblText", ((long)i).ToBytes(), "boy", "UIO");
                }

                tran.Commit();

                //for (int i = 0; i < 10; i++)
                //{
                //    var pk = i.To_4_bytes_array_BigEndian().Concat((i * 10).To_4_bytes_array_BigEndian());
                //    tran.TextInsert(tableNameTs, pk, "", $"#PK_{i}");
                //tran.Insert<byte[], int>(tableName, pk, i,);
                //}
                //tran.Commit();
            }


                return;
            Task.Run(() =>
            {
                using (var t = xfre.GetTransaction())
                {
                    t.RemoveAllKeys("t1", true);
                }
            });
        }

        public void TEST_RestoreTable()
        {
            DBreeze.Diagnostic.SpeedStatistic.ToConsole = false;
            if (xfre == null)
                xfre = new DBreezeEngine(@"D:\temp\DBR1");

            using (var tran = xfre.GetTransaction())
            {
                tran.RemoveAllKeys("testv001", true);
            }

            using (var tran = xfre.GetTransaction())
            {
                tran.Insert<int, int>("testv001", 1, 1);
                tran.Insert<int, int>("testv001", 2, 1);
                tran.Insert<int, int>("testv001", 3, 1);
                tran.Commit();
            }

            using (var tran = xfre.GetTransaction())
            {
                tran.RemoveKey<int>("testv001", 2);
                tran.RemoveKey<int>("testv001", 3);
                tran.Commit();
            }

            //Checking size of "testv001"
            var p1 = xfre.Scheme.GetTablePathFromTableName("testv001");
            FileInfo fi1 = new FileInfo(p1);
            Console.WriteLine(fi1.Length); //186 bytes

            //It is a bit weird to run table defragmentation procedure for each delete command, 
            //so let's e.g. once per day, start a purge table procedure:
            using (var tran = xfre.GetTransaction())
            {
                tran.SynchronizeTables("testv001");

                //Removing temporary table
                tran.RemoveAllKeys("tmp_testv001", true);

                //Copying into temporary table all necessary data.
                //Note this process is hard to automate due to the heterogeneous nature of the data (datablocks, nested tables etc...)
                foreach (var row in tran.SelectForward<int, int>("testv001"))
                {
                    tran.Insert<int, int>("tmp_testv001", row.Key, row.Value); //Copy all what you need
                }
                //Commiting temp table
                tran.Commit();

                //Experimenting run, starting from version 1.094 
                //(Reading threads will wait, though some of them will fail with errors, cause some read-out links to the previous table have changed)                
                //We have solution for read-write table locking (search in documentation "Full tables locking inside of transaction.")
                tran.RestoreTableFromTheOtherFile("testv001", "tmp_testv001", true);
                //At this moment tmp_testv001 files will be cleared
            }

            //Checking content of "testv001"
            using (var tran = xfre.GetTransaction())
            {
                foreach (var row in tran.SelectForward<int, int>("testv001"))
                {
                    Console.WriteLine(row.Key);
                }

            }

            //Checking size of "testv001"
            FileInfo fi2 = new FileInfo(xfre.Scheme.GetTablePathFromTableName("testv001"));

            Console.WriteLine(fi2.Length); //93 bytes


            return;
        }


        DBreezeEngine xfre = null;
        private void btTest3_Click(object sender, RoutedEventArgs e)
        {
        //    return;
        //    TEST_RestoreTable();
        //    return;

            DBreeze.Diagnostic.SpeedStatistic.ToConsole = false;
            if (xfre == null)
                xfre = new DBreezeEngine(@"D:\temp\DBR1");

            return;

            xfre.Scheme.DeleteTable("tb");

            using (var tran = xfre.GetTransaction())
            {
                tran.SynchronizeTables("tb");
                var res1 = tran.ObjectInsert("tb", new DBreeze.Objects.DBreezeObject<byte[]>
                {
                    NewEntity = true,
                    Entity = new byte[] { 1,2,3},
                    Indexes = new List<DBreeze.Objects.DBreezeIndex>
                        {
                            new DBreeze.Objects.DBreezeIndex(1, 1) { PrimaryIndex = true },
                            new DBreeze.Objects.DBreezeIndex(2, 100) { AddPrimaryToTheEnd = true},
                        }
                });

                 tran.ObjectRemove("tb", 1.ToIndex(1));   // Test with ObjectRemove

                //var res2 = tran.ObjectInsert("tb", new DBreeze.Objects.DBreezeObject<byte[]>
                //{
                //    NewEntity = true,
                //    Entity = new byte[] { 1, 2, 3, 4 },
                //    Indexes = new List<DBreeze.Objects.DBreezeIndex>
                //        {
                //            new DBreeze.Objects.DBreezeIndex(1, 1) { PrimaryIndex = true },
                //            new DBreeze.Objects.DBreezeIndex(2, 150) { AddPrimaryToTheEnd = true},
                //        }
                //});
                tran.Commit();
            }
            var ba = 1.ToIndex(0);
            using (var tran = xfre.GetTransaction())
            {
                foreach (var row in tran.SelectForwardFromTo<byte[], byte[]>("tb", 1.ToIndex(0), true, 1.ToIndex(1000), true))
                {
                    var obj = row.ObjectGet<byte[]>().Entity;
                    if (obj == null) continue;
                    //Console.WriteLine($"By PK; id: {obj.Id}, value: {obj.Value}, idx: {row.Key.ToBytesStringDec("-")}");
                    Console.WriteLine($"idx: {row.Key.ToBytesStringDec("-")}  val: {obj.ToBytesStringDec("-")}");
                }
                foreach (var row in tran.SelectForwardFromTo<byte[], byte[]>("tb", 2.ToIndex(0), true, 2.ToIndex(1000), true))
                {
                    var obj = row.ObjectGet<byte[]>().Entity;
                    if (obj == null) continue;
                    //Console.WriteLine($"By second index; id: {obj.Id}, value: {obj.Value}, idx: {row.Key.ToBytesStringDec("-")}");
                    Console.WriteLine($"idx: {row.Key.ToBytesStringDec("-")};  val: {obj.ToBytesStringDec("-")}");
                }
            }




            return;
            //using (var t = xfre.GetTransaction())
            //{
            //    for (int i = 0; i < 20; i++)
            //    {
            //        t.Insert<int, int>("t1", i, i);
            //    }

            //    t.Commit();
            //}

            Task.Run(() =>
            {
                using (var t = xfre.GetTransaction())
                {
                   foreach(var row in t.SelectForward<int,int>("t1"))
                    {
                        Console.WriteLine("Key: " + row.Key);
                        Thread.Sleep(500);
                    }
                }
            });



            return;
            using (var t = xfre.GetTransaction())
            {
                for(int i=0;i<20;i++)
                {
                    t.Insert<int, int>("t1", i, i);
                }
                
                t.Commit();
            }





            return;
          

            using (var t = xfre.GetTransaction())
            {               

                t.Insert<int, string>("t1", 1, "test1");
                t.Insert<int, string>("t1", 2, "test2");
                t.Insert<int, string>("t1", 3, "test3");
                

                t.Commit();
            }

            using (var t = xfre.GetTransaction())
            {
               
                //t.RemoveAllKeys("t1", true, () =>
                //{
                //    t.Insert<int, string>("t1", 2, "test2");
                //    t.Insert<int, string>("t1", 3, "test3");
                //});

                t.Commit();
            }

            using (var t = xfre.GetTransaction())
            {
                foreach(var r in t.SelectBackward<int,string>("t1"))
                {
                    Console.WriteLine(r.Key);
                }
            }
            return;

            using (var t = xfre.GetTransaction())
            {
                t.SynchronizeTables("t1", "t2");

                t.Insert<byte[], string>("t1", new byte[] { 1 }, "test");
                t.Insert<byte[], string>("t2", new byte[] { 1 }, "t1t");

                t.Commit();             
            }

            return;

            //using (var t = xfre.GetTransaction())
            //{
            //    t.Insert<byte[], byte[]>("t1", "abc".To_UTF8Bytes(), null);
            //    t.Insert<byte[], byte[]>("t1", "abcd".To_UTF8Bytes(), null);
            //    t.Insert<byte[], byte[]>("t1", "abce".To_UTF8Bytes(), null);
            //    t.Insert<byte[], byte[]>("t1", "abcd1".To_UTF8Bytes(), null);
            //    t.Insert<byte[], byte[]>("t1", "abcd2".To_UTF8Bytes(), null);

            //    //t.Insert<byte[], byte[]>("t1", "abahjk".To_UTF8Bytes(), null);
            //    //t.Insert<byte[], byte[]>("t1", "abejk".To_UTF8Bytes(), null);
            //    //t.Insert<byte[], byte[]>("t1", "abr".To_UTF8Bytes(), null);
            //    //t.Insert<byte[], byte[]>("t1", "abdfsdf".To_UTF8Bytes(), null);
            //    //t.Insert<byte[], byte[]>("t1", "abjkgfsdf".To_UTF8Bytes(), null);

            //    //t.Insert<byte[], byte[]>("t1", "aaejk".To_UTF8Bytes(), null);
            //    //t.Insert<byte[], byte[]>("t1", "amefjk".To_UTF8Bytes(), null);

            //    t.Commit();
            //}

            //return;

            //using (var t = xfre.GetTransaction())
            //{
            //    t.Insert<byte[], byte[]>("t1", "bde".To_UTF8Bytes(), null);

            //    foreach (var r in t.SelectForward<byte[], byte[]>("t1"))
            //    {  
            //        if (!r.Key.Substring(0, 3)._ByteArrayEquals("abc".To_UTF8Bytes()))
            //            break;
            //        Debug.WriteLine(r.Key.ToUTF8String());
            //    }

            //    t.ReadVisibilityScopeModifier_GenerateNewTableForRead = true;
            //    foreach (var r in t.SelectForward<byte[], byte[]>("t1", true))
            //        Debug.WriteLine(r.Key.ToBytesString());

            //    foreach (var r in t.SelectForward<byte[], byte[]>("t1", true))
            //        Debug.WriteLine(r.Key.ToBytesString());

            //    t.ReadVisibilityScopeModifier_DirtyRead = true;
            //    foreach (var r in t.SelectForward<byte[], byte[]>("t1", true))
            //        Debug.WriteLine(r.Key.ToBytesString());

            //    t.ReadVisibilityScopeModifier_GenerateNewTableForRead = false;

            //    foreach (var r in t.SelectForward<byte[], byte[]>("t1", false))
            //        Debug.WriteLine(r.Key.ToBytesString());

            //    foreach (var r in t.SelectForward<byte[], byte[]>("t1", true))
            //        Debug.WriteLine(r.Key.ToBytesString());

            //    foreach (var r in t.SelectForward<byte[], byte[]>("t1", true))
            //        Debug.WriteLine(r.Key.ToBytesString());
            //}

            //return;

            //using (var t = xfre.GetTransaction())
            //{
            //    //foreach (var r in t.SelectForward<byte[], byte[]>("t1"))
            //    //    Debug.WriteLine(r.Key.ToBytesString());

            //    foreach (var r in t.SelectForwardStartFrom<byte[], byte[]>("t1", "ab".To_UTF8Bytes(), false))
            //        Debug.WriteLine(r.Key.ToUTF8String());

            //    /*
            //     abahjk
            //    abc
            //    abdfsdf
            //    abejk
            //    abjkgfsdf
            //    abr
            //    //must be filtered
            //amefjk    
            // */
            //}


            //return;

            //List<byte[]> input = new List<byte[]>(){
            //    new byte[] { 1, 2, 4 }, 
            //    new byte[] { 1, 2, 3 },
            //    new byte[] { 1, 2, 3, 5 }
            //    };

            //foreach (var r1 in input.OrderByDescending(x => x, new ByteListComparer()))
            //    Debug.WriteLine(r1.ToBytesString());

            //DGNode n1 = null;
            //DGNode n2 = null;
            //DGNode n3 = null;
            //List<DGNode> ln = new List<DGNode>();
            //DGraph graph = null;

            //var hash = DBreeze.Utils.Hash.MurMurHash.MurmurHash3("Hello my dear valentine".To_UTF8Bytes());
            //byte[] bha = hash.To_4_bytes_array_BigEndian();

            //return;

            //using (var t = xfre.GetTransaction())
            //{
            //    graph = new DGraph(t, "g1");

            //    //foreach (var n in graph.GetNode("Tomato"))
            //    //foreach (var n in graph.GetNode("Vegetables"))
            //    //{
            //    //    Debug.WriteLine(n.ExternalId.ToUTF8String() + "_" + n.InternalId);

            //    //    //foreach (var nx in n.GetKid("Vasiliy Ivanov"))
            //    //    foreach (var nx in n.GetKid("Cucumber"))
            //    //    {
            //    //        Debug.WriteLine(nx.ExternalId.ToUTF8String() + "_" + nx.InternalId);
            //    //    }
            //    //}

            //    foreach (var n in graph.GetNode("Vasiliy Ivanov"))
            //    {
            //        Debug.WriteLine(n.ExternalId.ToUTF8String() + "_" + n.InternalId);

            //        //foreach (var nx in n.GetKid("Vasiliy Ivanov"))
            //        foreach (var nx in n.GetKid("Cucumber"))
            //        {
            //            Debug.WriteLine(nx.ExternalId.ToUTF8String() + "_" + nx.InternalId);
            //        }
            //    }
            //}

            //return;


            //using (var t = xfre.GetTransaction())
            //{
            //    graph = new DGraph(t, "g1");

            //    var n = graph.GetNode<string>("Digits").GetKid<int>(17).FirstOrDefault();
                
            //}

            //Debug.WriteLine("...........DONE..............");
            //return;

            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            //using (var t = xfre.GetTransaction())
            //{
            //    t.SynchronizeTables("g1");
            //    graph = new DGraph(t, "g1");

            //    //t.Technical_SetTable_OverwriteIsNotAllowed("g1");

            //    List<DGNode> ml = new List<DGNode>();
            //    //for (int ioii = 1; ioii < 10000; ioii++)    //a: 1; Time: 2876 ms; 7859532 ticks 
            //    //    ml.Add(graph.NewNode<int>(ioii));

            //    for (int ioii = 20000; ioii < 30000; ioii++)    //a: 1; Time: 2876 ms; 7859532 ticks 
            //        ml.Add(graph.NewNode<int>(ioii));

            //    graph.GetNode<string>("Digits")
            //      .AddKids(ml).Update();

            //    //graph.GetNode<string>("Digits")
            //    //    .AddKids(new List<DGNode> {
            //    //    graph.NewNode<int>(1),
            //    //    graph.NewNode<int>(2),
            //    //    graph.NewNode<int>(3),
            //    //    graph.NewNode<int>(4),
            //    //    graph.NewNode<int>(5),
            //    //    graph.NewNode<int>(6),
            //    //}).Update();

            //    t.Commit();
            //}
            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("a",true);
            //Debug.WriteLine("...........DONE..............");
            //return;



            //using (var t = xfre.GetTransaction())
            //{
            //    graph = new DGraph(t, "g1");
                
            //    var n = graph.GetNode<string>("Tomato").GetParent<string>("Vasiliy Ivanov").FirstOrDefault();
            //    n = graph.GetNode<string>("Vasiliy Ivanov").GetKid<string>("Tomato").FirstOrDefault();
            //    n = graph.GetNode<string>("Tomato").GetParent<string>("Masha Petrova").FirstOrDefault();
            //    n = graph.GetNode<string>("Tomato").GetParent<string>("Petric Pyatochkin").FirstOrDefault();
            //}

            //Debug.WriteLine("...........DONE..............");
            //return;

            //using (var t = xfre.GetTransaction())
            //{
            //    t.SynchronizeTables("g1");
            //    graph = new DGraph(t, "g1");
                
            //    graph.GetNode<string>("Vegetables")
            //        .AddKids(new List<DGNode> {
            //        graph.NewNode<string>("Tomato"),
            //        graph.NewNode<string>("Cabbage"),
            //        graph.NewNode<string>("Cucumber")
            //    }).Update();
                
            //    graph.GetNode<string>("People")              
            //    .AddKids(new List<DGNode> {                    
            //        graph.NewNode<string>("Vasiliy Ivanov")
            //        .AddKids(new List<DGNode>
            //        {
            //            graph.GetNode<string>("Tomato"),
            //            graph.GetNode<string>("Cucumber"),                        
            //        }),
            //        graph.NewNode<string>("Masha Petrova").AddKids(new List<DGNode>
            //        {
            //            graph.GetNode<string>("Tomato"),                                          
            //        }),
            //        graph.NewNode<string>("Petric Pyatochkin").AddKids(new List<DGNode>
            //        {
            //            graph.GetNode<string>("Cabbage"),
            //            graph.GetNode<string>("Cucumber")
            //        })
            //    }).Update();                

            //    t.Commit();
            //}
            //Debug.WriteLine("...........DONE..............");
            //return;



            //using (var t = xfre.GetTransaction())
            //{
            //    graph = new DGraph(t, "g1");

            //   // var mng = graph.GetOrCreateNode<string>("Vegetables");


            //    //var nnn = graph.GetNode("Tomato").FirstOrDefault();
            //    //var x= nnn.GetParent("Vegetables").FirstOrDefault();
            //}
            //Debug.WriteLine("...........DONE..............");
            //return;



            //using (var t = xfre.GetTransaction())
            //{
            //    t.SynchronizeTables("g1");

            //    //For extra speed (if necessary only)
            //    //t.Technical_SetTable_OverwriteIsNotAllowed("g1");

            //    //One DGraph instance per DBreeze table
            //    graph = new DGraph(t, "g1");


            //    //graph.GetOrCreateNode("Vegetables")

            //    //n1 = new DGNode("Vegetables");
            //    //n1.LinksKids = new List<DGNode>{
            //    //    new DGNode("Tomato"),
            //    //    new DGNode("Cabbage"),
            //    //    new DGNode("Cucumber")
            //    //};
            //    //n1 = graph.AddNode(n1); //Global node



            //    //n2 = new DGNode("People");

            //    //var nnn = graph.GetNode("Vegetables").FirstOrDefault();

            //    //n2.LinksKids = new List<DGNode>{
            //    //    new DGNode("Vasiliy Ivanov")
            //    //    {  LinksKids = new List<DGNode>
            //    //        {
            //    //            nnn.GetKid("Tomato").FirstOrDefault(),
            //    //            nnn.GetKid("Cucumber").FirstOrDefault()
            //    //        }
            //    //    }
            //    //    ,
            //    //    new DGNode("Masha Petrova"),
            //    //    new DGNode("Petric Pyatochkin"),
            //    //};

            //    //n2 = graph.AddNode(n2); //Global node              

            //    //t.Commit();
            //}

            //using (var t = xfre.GetTransaction())
            //{
            //    t.Insert<byte[], byte[]>("t1", new byte[] { 1, 2, 4 }, null);
            //    t.Insert<byte[], byte[]>("t1", new byte[] { 1, 2, 3 }, null);
            //    t.Insert<byte[], byte[]>("t1", new byte[] { 1, 2, 3, 5 }, null);
            //    t.Commit();
            //}

            //using (var t = xfre.GetTransaction())
            //{
            //    foreach (var r in t.SelectForward<byte[], byte[]>("t1"))
            //        Debug.WriteLine(r.Key.ToBytesString());
            //}

            Debug.WriteLine("...........DONE..............");
            return;
            return;
            //GC.Collect();
            //return;

            //_ft.TEST_REMOVE();
            

            //_ft.TEST_SPIN_PRINT();

            //double ul = -456832178978879.549879865421D;
            //ul = System.Double.NaN;
            //byte[] tr = DBreeze.DataTypes.DataTypesConvertor.ConvertValue<double>(ul);

            //return;

            //float ul = -45683.54945F;
            //byte[] ba = ul.To_4_bytes_array_BigEndian();


            //float ul1 = ba.To_Float_BigEndian();

            //if (ul != ul1)
            //{
            //    Console.WriteLine("error");
            //}

            //return;
            //Checking pattern



            //string a = "abcde#/";
            //int ind=a.IndexOf('/', 5);
            //char b = a[ind];

            //bool res = false;
            //res = DBreeze.DataTypes.DbUserTableName.PatternsIntersect("a#/cars", "a12354/c");
            //res = DBreeze.DataTypes.DbUserTableName.PatternsIntersect("a#/cars", "a123#/cars");
            //res = DBreeze.DataTypes.DbUserTableName.PatternsIntersect("a#/cars", "a1/cars");
            //res = DBreeze.DataTypes.DbUserTableName.PatternsIntersect( "a/cars","a#/cars");
            //Console.WriteLine(res);

            //DBreeze.DataTypes.DbUserTables.UserTablesPatternsCheck();
            //DBreeze.SchemaInternal.DbUserTables.PatternsIntersectionsCheck();
            //DBreeze.DataTypes.DbUserTables.PatternsIntersectionsCheckSpeed();

            return;
            //decimal dd = 1.25465464646546848213135498746897M; //prec 1 scale 28
            ////decimal dd = 1254654646465468482131354987.46897M; //prec 29 scale 1
            ////bool res = Decimal.TryParse("123e-1286540", System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture,out dd);
            ////Decimal.TryParse("12,3", System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dd);

            //byte prec=0;
            //byte scale=0;

            //System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            //sw.Reset();
            //sw.Start();
            //for (int i = 0; i < 1000000; i++)
            //{
            //    System.Data.SqlTypes.SqlDecimal sd = new System.Data.SqlTypes.SqlDecimal(dd);
            //    prec = sd.Precision;
            //    scale = sd.Scale;
            //    //res = Decimal.TryParse("123e-1286540", System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dd);
            //    //res = Decimal.TryParse("123e+2", System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dd);
            //}
            //sw.Stop();
            //Console.WriteLine("Elapsed: {0} ms", sw.ElapsedMilliseconds);

            //return;




            Dictionary<double, byte[]> _xd = new Dictionary<double, byte[]>();
            double d=0;

            d=0;
            _xd.Add(d,GetBytes(d));
            d = -1;
            _xd.Add(d, GetBytes(d));            
            d = 1;
            _xd.Add(d, GetBytes(d));
            d = -10000;
            _xd.Add(d, GetBytes(d));
            d = 10000;
            _xd.Add(d, GetBytes(d));

            d = -12.1145;
            _xd.Add(d, GetBytes(d));
            d = 12.1145;
            _xd.Add(d, GetBytes(d));


            PrintOutDoubles(_xd);

            return;
            //byte bt = 100;

            //byte[] ret = bt.ToBitArray();

            //return;

            //for (int i = 0; i < 256; i++)
            //{
            //    Console.WriteLine("I: {0}; I/2: {1}; I%2: {2}; Bits: {3}; |: {4}; &: {5}; ^: {6}", i, i / 2, i % 2,
            //        ((byte)i).ToBitArray().ToBytesString(""),
            //        ((byte)(i | 255)).ToBitArray().ToBytesString(""),
            //        ((byte)(i & 255)).ToBitArray().ToBytesString(""),
            //        ((byte)(i ^ 255)).ToBitArray().ToBytesString("")
            //        );
            //}
            //int a = 17;
            //int b = a / 2;
            //int c = a % 2;
            //int g = 0;

            //if (c > 0)
            //    g = (b) * 2;

            //Console.WriteLine(g);
           // double d = 1;
           

           //// d = Convert.ToDouble("4.65654846845667897987987E+136",System.Globalization.CultureInfo.InvariantCulture);
           // d = -24565465464654654646465465.45646546546546546546465465564654654687468432131549876543313549846465465;
           // d = 0.00000000000000044567898798798797979798;
           // //d = Convert.ToDouble("-24565465464654654646465465.45646546546546546546465465564654654687468432131549876543313549846465465", System.Globalization.CultureInfo.InvariantCulture);
           // d = Convert.ToDouble("2.5E-6", System.Globalization.CultureInfo.InvariantCulture);

           // System.IO.MemoryStream ms = new System.IO.MemoryStream();


            // System.IO.FileStream fs = new System.IO.FileStream(@"D:\temp\DBreezeTest\binary.txt", System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.Read);
           // //System.IO.BinaryWriter writer = new System.IO.BinaryWriter(fs);
           // System.IO.BinaryWriter writer = new System.IO.BinaryWriter(ms);

           // writer.Write(d);

           // writer.Flush();
           // ms.ToArray();

           // byte[] v = new byte[8];
           // v = ms.ToArray();
           // //ms.Read(v, 0, v.Length);
            
           // d = 1;

           // writer.Write(d);
           // writer.Flush();

           // //ms.Read(v, 0, v.Length);
           // v = ms.ToArray();

           // writer.Close();
            
           // fs.Close();
           // fs.Dispose();
            //return;

            _dC.Clear();
            _d1C.Clear();
            _fC.Clear();

            //addFC(System.Single.MaxValue);
            //addFC(System.Single.MinValue);
            //addFC(0F);
            //addFC(-12548.14578F);
            //addFC(-12548.14568F);
            //addFC(-11548.14569F);
            //addFC(-11548.14568F);
            //addFC(-9548.14568F);
            //addFC(12548.14568F);
            //addFC(11548.14578F);
            //addFC(9548.14568F);
            //addFC(9548.14567F);
            //addFC(9548.145671F);
            //addFC(9548.145691F);
            

            //printFC();
            //return;


            //addD1C(System.Double.MaxValue);
            //addD1C(System.Double.MinValue);
            //addD1C(0D);
            //addD1C(-15D);
            //addD1C(-12548.14578D);
            //addD1C(-12548.14568D);
            //addD1C(-11548.14569D);
            //addD1C(-11548.14568D);
            //addD1C(-9548.14568D);
            //addD1C(12548.14568D);
            //addD1C(11548.14578D);
            //addD1C(9548.14568D);
            //addD1C(9548.14567D);
            //addD1C(9548.145671D);
            //addD1C(9548.145691D);

            ////BitConverter.ToSingle(

            ////addDC(System.Single.MaxValue);
            ////addDC(System.Single.MinValue);
            ////addDC(System.Double.MaxValue);
            ////addDC(System.Double.MinValue);
            ////addDC(-20);
            ////addDC(-22.15456584);
            ////addDC(-21.15456585);
            ////addDC(-20.05456585);
            ////addDC(0);
            ////addDC(20);
            ////addDC(200000000);

            //printD1C();

            //All vars are not good main idea is to use decimal: 28-29 digits, double 15-16, float 7

            addDC(System.Double.MaxValue);
            addDC(System.Double.MinValue);
            addDC(0D);
            addDC(-10.123456789123456789123456789D);
            addDC(-123456789123456789123456789123456789.123456789123456789123456789D);
            addDC(-15D);
            addDC(-12548.14578D);
            addDC(-12548.14568D);
            addDC(-11548.14569D);
            addDC(-11548.14568D);
            addDC(-9548.14568D);
            addDC(12548.14568D);
            addDC(11548.14578D);
            addDC(9548.14568D);
            addDC(9548.14567D);
            addDC(9548.145671D);
            addDC(9548.145691D);

            printDC();
        }

        private void addFC(float fl)
        {
            byte[] btF = FloatToBytes(fl);
            _fC.Add(BitConverter.ToSingle(btF, 0), btF);
        }

        private void addDC(double d)
        {
            _dC.Add(BitConverter.DoubleToInt64Bits(d), d);
        }

        private void addD1C(double d)
        {
            _d1C.Add(d, DoubleToBytes(d));
        }

        private void printD1C()
        {
            foreach (var res in _d1C.OrderBy(r=>r.Key))
            {
                Console.WriteLine("{0}; {1} ", res.Value.ToBytesString(""),res.Key);
            }
        }

        private void printDC()
        {
            foreach (var res in _dC.OrderBy(r=>r.Key))
            {
                Console.WriteLine("{0}; {1} ", res.Value,res.Key);
            }
        }

        private void printFC()
        {
            foreach (var res in _fC.OrderBy(r => r.Key))
            {
                Console.WriteLine("{0}; {1} ", res.Key, res.Value.ToBytesString("") );
            }
        }

        /// <summary>
        /// Decimal to Bye array
        /// </summary>
        /// <param name="dec">decimal</param>
        /// <returns>byte array</returns>
        public static byte[] DecimalToBytes(Decimal dec)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            {
                using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream))
                {
                    writer.Write(dec);

                    return stream.ToArray();
                }
            }
        }

        /// <summary>
        /// Float to Bye array
        /// </summary>
        /// <param name="dec">decimal</param>
        /// <returns>byte array</returns>
        public static byte[] FloatToBytes(float dec)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            {
                using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream))
                {
                    writer.Write(dec);

                    return stream.ToArray();
                }
            }
        }

        /// <summary>
        /// Double to Bye array
        /// </summary>
        /// <param name="dec">decimal</param>
        /// <returns>byte array</returns>
        public static byte[] DoubleToBytes(double dec)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            {
                using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream))
                {
                    writer.Write(dec);

                    return stream.ToArray();
                }
            }
        }

        /// <summary>
        /// Byte array to Decimal
        /// </summary>
        /// <param name="src">byte array</param>
        /// <returns>decimal</returns>
        public static Decimal BytesToDecimal(byte[] src)
        {
            if (src.Length == 1)
            {
                return Decimal.Parse(((char)src[0]).ToString());
            }

            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(src))
            {
                using (System.IO.BinaryReader reader = new System.IO.BinaryReader(stream))
                {
                    return reader.ReadDecimal();
                }

            }
        }


        private void GGG(double a)
        {
            Console.WriteLine("ConvTo: {0}; DoubleVal: {1}; ",BitConverter.DoubleToInt64Bits(a), a);
        }


        private void btTest4_Click(object sender, RoutedEventArgs e)
        {
            _ft.StartDBreeze();
        }
                

        private void btTest5_Click(object sender, RoutedEventArgs e)
        {          

            _ft.StartTest();
           
        }



        /// <summary>
        /// Testing simulteneously open files by .NET FileStream
        /// 1MLN - OK
        /// </summary>
        private void TestQuantityOfOpenFiles()
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            /*Testing quantity of Files which can be opened by .NET*/
            FileStream fs = null;
            string pd = @"D:\temp\DBreezeTest\1\";
            Dictionary<int, FileStream> dfs = new Dictionary<int, FileStream>();

            if (System.IO.Directory.Exists(pd))
                System.IO.Directory.Delete(pd, true);

            System.IO.Directory.CreateDirectory(pd);

            int t1 = 0;
            int t2 = 1000000;   //Works well
            //int t2 = 5;

            for (int i = 0; i <= t2; i++)
            {
                try
                {
                    fs = new FileStream(pd + i.ToString(), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                    dfs.Add(i, fs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERR ENDED WITH {0} open files", i);
                }
            }
            //Console.WriteLine("Done");




            //Console.WriteLine("RW " + t1.ToString());
            //dfs[t1].Write(new byte[] { 1, 2, 3 }, 0, 3);
            //dfs[t1].Flush();
            //byte[] r = new byte[3];
            //dfs[t1].Position = 0;
            //int q = dfs[t1].Read(r, 0, 3);
            //Console.WriteLine(r.ToBytesString(""));

            //dfs[t1].Close();
            //dfs[t1].Dispose();

            //Console.WriteLine("RW " + t2.ToString());
            //dfs[t2].Write(new byte[] { 1, 2, 3 }, 0, 3);
            //dfs[t2].Flush();
            ////byte[] r = new byte[3];
            //dfs[t2].Position = 0;
            //q = dfs[t2].Read(r, 0, 3);
            //Console.WriteLine(r.ToBytesString(""));

            //dfs[t2].Close();
            //dfs[t2].Dispose();


            sw.Stop();
            Console.WriteLine("CREATE FILE DONE " + sw.ElapsedMilliseconds.ToString());

            Console.WriteLine("***************   testing access");

            Random rnd = new Random();
            int fIn = 0;
            byte[] r = null;
            int q = 0;
            for (int i = 0; i < 20; i++)
            {
                fIn = rnd.Next(t2 - 1);

                dfs[fIn].Write(new byte[] { 1, 2, 3 }, 0, 3);
                dfs[fIn].Flush();
                r = new byte[3];
                dfs[fIn].Position = 0;
                q = dfs[fIn].Read(r, 0, 3);
                Console.WriteLine("RW: {0}; V: {1}", fIn, r.ToBytesString(""));
            }

        }



        #region "ROW 3"
        private void btTest7_Click(object sender, RoutedEventArgs e)
        {

            _ft.StartThread1();

            //Action a = () =>
            //{
            //    _ft.TEST15_ReadParallel();
            //};

            //a.DoAsync();
            
        }

        private void btTest8_Click(object sender, RoutedEventArgs e)
        {
            _ft.StartThread2();
            //Action a = () =>
            //{
            //    _ft.TEST15_RemoveAllWithFileRecreation();
            //};

            //a.DoAsync();
            
        }

        private void btTest9_Click(object sender, RoutedEventArgs e)
        {
            _ft.StartThread3();
            //Action a = () =>
            //{
            //   // _ft.TEST15_Write();
            //    _ft.TESTREADSYNCHRO_Parallel();
            //};

            //a.DoAsync();
        }

     

        private void btTest11_Click(object sender, RoutedEventArgs e)
        {
            
        }

        private void btTest12_Click(object sender, RoutedEventArgs e)
        {
           

        }
        #endregion

        private void btTest6_Click(object sender, RoutedEventArgs e)
        {
            DocuExamples de = new DocuExamples(@"D:\temp\DBreezeExample");
            //de.Example_InsertingData();
            //de.Example_FetchingRange1();
            //de.Example_InsertingObject();
            //de.Example_NonUniqueKey();
            de.Example_GettingStartedWithNestedTables();
        }
        
        private void btTest13_Click(object sender, RoutedEventArgs e)
        {
            //Benchmark bm=new Benchmark(@"D:\temp\DBreezeBenchmark");
            //Benchmark bm = new Benchmark(@"H:\c\tmp\dbtest");//C:\temp\Testers\dbTest
            Benchmark bm = new Benchmark(@"C:\temp\Testers\dbTest");//C:\temp\Testers\dbTest
            bm.Start();
    
        }

        private void btRestoreBackup_Click(object sender, RoutedEventArgs e)
        {           
            DbreezeBackupRestorerWindow win = new DbreezeBackupRestorerWindow();
            win.ShowDialog();
        }


        class MyTask
        {
            public long Id { get; set; }            
            public string Description { get; set; } = "";
            public string Notes { get; set; } = "";
        }

        //int inDeferredIndexer = 0;

        //void DeferredIndexer()
        //{
        //    if (System.Threading.Interlocked.CompareExchange(ref inDeferredIndexer, 1, 0) != 0)
        //        return;

        //    //Only one indexer instance can run
        //    System.Threading.Tasks.Task.Run(() =>
        //    {
                
        //        int maximalIterations = 10; //Iterations then a breath
        //        int currentItter = 0;
        //        Dictionary<DateTime,byte[]> toBeIndexed = new Dictionary<DateTime, byte[]>();
        //        HashSet<string> tbls = new HashSet<string>();
        //        Dictionary<string, List<byte[]>> dPendingBack = new Dictionary<string, List<byte[]>>();

        //        while (true)
        //        {
        //            currentItter = 0;
        //            toBeIndexed.Clear();
        //            tbls.Clear();

        //            //Short synchronized transaction
        //            using (var tran = textsearchengine.GetTransaction())
        //            {

        //                //we want to store text index in table “TaskFullTextSearch” and task itself in table "Tasks"
        //                tran.SynchronizeTables("DeferredIndexer"); //MUST BE HERE, not to lose elements !!!!check it

        //                foreach (var el in tran.SelectForward<DateTime, byte[]>("DeferredIndexer").Take(maximalIterations))
        //                {
        //                    currentItter++;
        //                    toBeIndexed.Add(el.Key, el.Value);
        //                    DBreeze.Utils.Biser.Decode_DICT_PROTO_STRING_BYTEARRAYHASHSET(el.Value, dPendingBack, Compression.eCompressionMethod.NoCompression);
        //                }

        //                if (currentItter == 0)
        //                {
        //                    //releasing DeferredIndexer
        //                    inDeferredIndexer = 0;
        //                    return;
        //                }
        //            }

        //            using (var tran = textsearchengine.GetTransaction())
        //            {
                        
        //                tran.SynchronizeTables("TaskFullTextSearch");       //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

        //                Dictionary<string, List<byte[]>> dPendingBack = new Dictionary<string, List<byte[]>>();
        //                foreach (var el in toBeIndexed)
        //                {
        //                    dPendingBack.Clear();
        //                    //deserialization of tables and documents to be indexed, using internal DBreeze deserializer (can be used outer deserializers)
                            

        //                    foreach (var t in dPendingBack)
        //                    {
        //                        switch (t.Key)
        //                        {
        //                            case "Tasks":
        //                                MyTask tsk = null;
        //                                foreach (var tId in t.Value)
        //                                {
        //                                    //Get task from table and using its Description and notes
        //                                    tran.TextInsertToDocument("TaskFullTextSearch", tId, "tsk.Descriptio zero" + " " + "tsk.Notes well ", new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false });
        //                                }
        //                                break;
        //                        }
        //                    }
        //                }

        //                tran.Commit();
        //            }


        //            using (var tran = textsearchengine.GetTransaction())
        //            {
        //                tran.SynchronizeTables("PendingTextSearchIndexing");

        //                foreach (var el in toBeIndexed)
        //                    tran.RemoveKey<byte[]>("PendingTextSearchIndexing", el);

        //                tran.Commit();

        //                if (currentItter < maximalIterations)
        //                {
        //                    //End of PendingIndexer
        //                    inDeferredIndexer = 0;
        //                }
        //            }

        //            if (inDeferredIndexer == 0)
        //                return;
        //            else
        //            {
        //                System.Threading.Thread.Sleep(1000);    //Giving a breath, before next itteration
        //            }
        //        }//wo while    
        //    });

        //}

        DBreezeEngine textsearchengine = null;
        private void btTestSearchText_Click(object sender, RoutedEventArgs e)
        {

            //Dictionary<string, HashSet<uint>> d = new Dictionary<string, HashSet<uint>>();
            //d.Add("t1", null);
            //d.Add("t2", new HashSet<uint> { 4, 5, 6 });

            //var btx = Biser.Encode_DICT_PROTO_STRING_UINTHASHSET(d, Compression.eCompressionType.NoCompression);
            //Dictionary<string, HashSet<uint>> d1 = new Dictionary<string, HashSet<uint>>();

            //Biser.Decode_DICT_PROTO_STRING_UINTHASHSET(btx, d1, Compression.eCompressionType.NoCompression);

            //return;
            if (textsearchengine == null)
            {
                textsearchengine = new DBreezeEngine(@"D:\temp\DBR1\");
            }
           
            using (var tran = textsearchengine.GetTransaction())
            {
                tran.TextInsert("transtext", new byte[] { 1 }, "Apple Banana Сегодня день 周杰伦", "#LG1 #LG2");
                tran.TextInsert("transtext", new byte[] { 2 }, "Banana MANGO", "#LG2 #LG3");
                tran.Commit();

            }

            using (var tran = textsearchengine.GetTransaction())
            {
                //var block = tran.TextSearch("transtext").Block("Apple");
                var block = tran.TextSearch("transtext").Block("周杰伦");
                Console.WriteLine($"---- {block.GetDocumentIDs().Count()} ---");//returns 1
            }

            return;

            //Testing External indexer concept.
            //Table where we will store Docs to be indexed
            //using (var tran = textsearchengine.GetTransaction())
            //{

            //    //we want to use table PendingTextSearchIndexing to communicate with AutomaticIndexer (so we will write into it also)
            //    tran.SynchronizeTables("Tasks", "TaskText");
        
                
            //    //Storing task
            //    tsk = new MyTask()
            //    {
            //        Id = 1,
            //        Description = "Starting with the .NET Framework version 2.0, well if you derive a class from Random and override the Sample method, the distribution provided by the derived class implementation of the Sample method is not used in calls to the base class implementation of the NextBytes method. Instead, the uniform",
            //        Notes = "distribution returned by the base Random class is used. This behavior improves the overall performance of the Random class. To modify this behavior to call the Sample method in the derived class, you must also override the NextBytes method"
            //    };
            //    //Inserting task value must be, of course tsk
            //    tran.Insert<long, byte[]>("Tasks", tsk.Id, null);

            //    tran.TextInsertToDocument("TaskText", tsk.Id.To_8_bytes_array_BigEndian(), tsk.Description + " " + tsk.Notes, 
            //        new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false, DeferredIndexing = true });

            //    tsk = new MyTask()
            //    {
            //        Id = 2,
            //        Description = "VI guess in Universal Apps for Xamarin you need to include the assembly when loading embedded resources. I had to change",
            //        Notes = "I work on.NET for UWP.This is super interesting and I'd well love to take a deeper look at it after the holiday. If "
            //    };
            //    tran.Insert<long, byte[]>("Tasks", tsk.Id, null);

            //    tran.TextInsertToDocument("TaskText", tsk.Id.To_8_bytes_array_BigEndian(), tsk.Description + " " + tsk.Notes,
            //      new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false, DeferredIndexing = true });

            //    tran.Commit();
            //}

            return;

                //using (var tran = textsearchengine.GetTransaction())
                //{
                //    tsk = new MyTask()
                //    {
                //        Id = 2,
                //        ExternalId = "x2",
                //        Description = "Very second task ",
                //        Notes = "small"
                //    };
                //    sb.Append(tsk.Description + " " + tsk.Notes);
                //    tran.InsertDocumentText("TaskFullTextSearch" + companyId, tsk.Id.To_8_bytes_array_BigEndian(), sb.ToString(), new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false });

                //    tran.Commit();
                //}

                //using (var tran = textsearchengine.GetTransaction())
                //{
                //    var resp = tran.TextSearch("TaskFullTextSearch", new DBreeze.TextSearch.TextSearchRequest()
                //    {
                //        // SearchLogicType = DBreeze.TextSearch.TextSearchRequest.eSearchLogicType.OR,
                //        SearchWords = "review1"
                //    });

                //}
                //return;

                SpeedStatistic.ToConsole = false;
            SpeedStatistic.StartCounter("a");

            //using (var tran = textsearchengine.GetTransaction())
            //{

            //    tsk = new MyTask()
            //    {
            //        Id = 3,
            //        Description = "test review",
            //        Notes = "metro"
            //    };
            //    tran.Insert<long, byte[]>("Tasks", tsk.Id, null);

            //    tran.TextInsertToDocument("TaskFullTextSearch", tsk.Id.To_8_bytes_array_BigEndian(), tsk.Description + " " + tsk.Notes, new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false, });
            //    //tran.TextAppendWordsToDocument("TaskFullTextSearch", tsk.Id.To_8_bytes_array_BigEndian(), "cheater", new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false, });
            //    //tran.TextRemoveWordsFromDocument("TaskFullTextSearch", tsk.Id.To_8_bytes_array_BigEndian(), "cheater", new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = true, });
           

            //    tran.Commit();
            //}
            //return;

            //using (var tran = textsearchengine.GetTransaction())
            //{

            //    //we want to store text index in table “TaskFullTextSearch” and task itself in table "Tasks"
            //    tran.SynchronizeTables("Tasks", "TaskFullTextSearch");

            //    //Storing task
            //    tsk = new MyTask()
            //    {
            //        Id = 1,                    
            //        Description = "Starting with the .NET Framework version 2.0, well if you derive a class from Random and override the Sample method, the distribution provided by the derived class implementation of the Sample method is not used in calls to the base class implementation of the NextBytes method. Instead, the uniform",
            //        Notes = "distribution returned by the base Random class is used. This behavior improves the overall performance of the Random class. To modify this behavior to call the Sample method in the derived class, you must also override the NextBytes method"
            //    };
            //    tran.Insert<long, byte[]>("Tasks", tsk.Id, null);

            //    //Creating text, for the document search. any word or word part (minimum 3 chars, check TextSearchStorageOptions) from Description and Notes will return us this document in the future
            //    tran.TextInsertToDocument("TaskFullTextSearch", tsk.Id.To_8_bytes_array_BigEndian(), tsk.Description + " " + tsk.Notes, new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false, });

                
            //    tsk = new MyTask()
            //    {
            //        Id = 2,                    
            //        Description = "VI guess in Universal Apps for Xamarin you need to include the assembly when loading embedded resources. I had to change",
            //        Notes = "I work on.NET for UWP.This is super interesting and I'd well love to take a deeper look at it after the holiday. If "
            //    };
            //    tran.Insert<long, byte[]>("Tasks", tsk.Id, null);                
            //    tran.TextInsertToDocument("TaskFullTextSearch", tsk.Id.To_8_bytes_array_BigEndian(), tsk.Description + " " + tsk.Notes, new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false, });
                                
                                
            //    tsk = new MyTask()
            //    {
            //        Id = 3,                    
            //        Description = "Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met",
            //        Notes = "This clause was objected to on the grounds that as people well changed the license to reflect their name or organization it led to escalating advertising requirements when programs were combined together in a software distribution: every occurrence of the license with a different name required a separate acknowledgment. In arguing against it, Richard Stallman has stated that he counted 75 such acknowledgments "
            //    };
            //    tran.Insert<long, byte[]>("Tasks", tsk.Id, null);
            //    tran.TextInsertToDocument("TaskFullTextSearch", tsk.Id.To_8_bytes_array_BigEndian(), tsk.Description + " " + tsk.Notes, new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false, });

            //    //Committing all together. 
            //    //Though its possible to build an automatic indexer for the huge text and call it in parallel thread and here to store only changed documentIDs which must be indexed.
            //    //All depends upon necessary insert speed.
            //    tran.Commit(); 
            //}

            SpeedStatistic.PrintOut("a",true);

        }
    }
}
