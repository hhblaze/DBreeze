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

using System.IO;
using DBreeze;
using DBreeze.Diagnostic;

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
        Dictionary<double,byte[]> _d1C = new Dictionary<double,byte[]>();

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
            foreach(var el in xd.OrderBy(r=>r.Key))
            {
                Console.WriteLine("D: {0}; B: {1}", el.Key, el.Value.ToBytesString(""));
            }
        }



        private void btTest3_Click(object sender, RoutedEventArgs e)
        {
            //GC.Collect();
            //return;

            _ft.TEST_REMOVE();
            

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

        private void btTest10_Click(object sender, RoutedEventArgs e)
        {

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
            Benchmark bm=new Benchmark(@"D:\temp\DBreezeBenchmark");
            //Benchmark bm = new Benchmark(@"S:\temp\DBreezeBenchmark");
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
            MyTask tsk = null;


            using (var tran = textsearchengine.GetTransaction())
            {
                var resp = tran.TextSearch("TaskText", new DBreeze.TextSearch.TextSearchRequest()
                {
                    // SearchLogicType = DBreeze.TextSearch.TextSearchRequest.eSearchLogicType.OR,
                    SearchWords = "Xamarin"
                });

            }

            return;

            //Testing External indexer concept.
            //Table where we will store Docs to be indexed
            using (var tran = textsearchengine.GetTransaction())
            {

                //we want to use table PendingTextSearchIndexing to communicate with AutomaticIndexer (so we will write into it also)
                tran.SynchronizeTables("Tasks", "TaskText");
        
                
                //Storing task
                tsk = new MyTask()
                {
                    Id = 1,
                    Description = "Starting with the .NET Framework version 2.0, well if you derive a class from Random and override the Sample method, the distribution provided by the derived class implementation of the Sample method is not used in calls to the base class implementation of the NextBytes method. Instead, the uniform",
                    Notes = "distribution returned by the base Random class is used. This behavior improves the overall performance of the Random class. To modify this behavior to call the Sample method in the derived class, you must also override the NextBytes method"
                };
                //Inserting task value must be, of course tsk
                tran.Insert<long, byte[]>("Tasks", tsk.Id, null);

                tran.TextInsertToDocument("TaskText", tsk.Id.To_8_bytes_array_BigEndian(), tsk.Description + " " + tsk.Notes, 
                    new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false, DeferredIndexing = true });

                tsk = new MyTask()
                {
                    Id = 2,
                    Description = "VI guess in Universal Apps for Xamarin you need to include the assembly when loading embedded resources. I had to change",
                    Notes = "I work on.NET for UWP.This is super interesting and I'd well love to take a deeper look at it after the holiday. If "
                };
                tran.Insert<long, byte[]>("Tasks", tsk.Id, null);

                tran.TextInsertToDocument("TaskText", tsk.Id.To_8_bytes_array_BigEndian(), tsk.Description + " " + tsk.Notes,
                  new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false, DeferredIndexing = true });

                tran.Commit();
            }

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

            using (var tran = textsearchengine.GetTransaction())
            {

                //we want to store text index in table “TaskFullTextSearch” and task itself in table "Tasks"
                tran.SynchronizeTables("Tasks", "TaskFullTextSearch");

                //Storing task
                tsk = new MyTask()
                {
                    Id = 1,                    
                    Description = "Starting with the .NET Framework version 2.0, well if you derive a class from Random and override the Sample method, the distribution provided by the derived class implementation of the Sample method is not used in calls to the base class implementation of the NextBytes method. Instead, the uniform",
                    Notes = "distribution returned by the base Random class is used. This behavior improves the overall performance of the Random class. To modify this behavior to call the Sample method in the derived class, you must also override the NextBytes method"
                };
                tran.Insert<long, byte[]>("Tasks", tsk.Id, null);

                //Creating text, for the document search. any word or word part (minimum 3 chars, check TextSearchStorageOptions) from Description and Notes will return us this document in the future
                tran.TextInsertToDocument("TaskFullTextSearch", tsk.Id.To_8_bytes_array_BigEndian(), tsk.Description + " " + tsk.Notes, new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false, });

                
                tsk = new MyTask()
                {
                    Id = 2,                    
                    Description = "VI guess in Universal Apps for Xamarin you need to include the assembly when loading embedded resources. I had to change",
                    Notes = "I work on.NET for UWP.This is super interesting and I'd well love to take a deeper look at it after the holiday. If "
                };
                tran.Insert<long, byte[]>("Tasks", tsk.Id, null);                
                tran.TextInsertToDocument("TaskFullTextSearch", tsk.Id.To_8_bytes_array_BigEndian(), tsk.Description + " " + tsk.Notes, new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false, });
                                
                                
                tsk = new MyTask()
                {
                    Id = 3,                    
                    Description = "Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met",
                    Notes = "This clause was objected to on the grounds that as people well changed the license to reflect their name or organization it led to escalating advertising requirements when programs were combined together in a software distribution: every occurrence of the license with a different name required a separate acknowledgment. In arguing against it, Richard Stallman has stated that he counted 75 such acknowledgments "
                };
                tran.Insert<long, byte[]>("Tasks", tsk.Id, null);
                tran.TextInsertToDocument("TaskFullTextSearch", tsk.Id.To_8_bytes_array_BigEndian(), tsk.Description + " " + tsk.Notes, new DBreeze.TextSearch.TextSearchStorageOptions() { FullTextOnly = false, });

                //Committing all together. 
                //Though its possible to build an automatic indexer for the huge text and call it in parallel thread and here to store only changed documentIDs which must be indexed.
                //All depends upon necessary insert speed.
                tran.Commit(); 
            }

            SpeedStatistic.PrintOut("a",true);

        }
    }
}
