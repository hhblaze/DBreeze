using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Threading;
using System.Diagnostics;
using System.IO;

using DBreeze;
using DBreeze.LianaTrie;
using DBreeze.Storage;
using DBreeze.Utils;
using DBreeze.Utils.Async;
using DBreeze.DataTypes;

//using Newtonsoft.Json;

namespace VisualTester
{
    public class FastTests
    {

        #region "LianaYaskaTrie tests"
        string LianaTrieFileName = @"D:\temp\DBreezeTest\fdbLtrie.txt";

        TrieSettings LTrieSettings = null;
        IStorage LTrieStorage = null;
        LTrie LTrie = null;

        bool initLTrieAscii = false;

        /// <summary>
        /// 
        /// </summary>
        private void InitLTrieAscii()
        {
            if (initLTrieAscii)
                return;

            //File.Delete(LianaTrieFileName);

            initLTrieAscii = true;

            LTrieSettings = new TrieSettings();
            LTrieStorage = new StorageLayer(LianaTrieFileName, LTrieSettings, new DBreezeConfiguration());
            //LTrieStorage = new TrieMemoryStorage(50 * 1024 * 1024, LTrieSettings);

            LTrie = new LTrie(LTrieStorage);
        }

        //private byte[] G(string table)
        //{
        //    return System.Text.Encoding.UTF8.GetBytes(table);
        //}

        //private byte[] GT(string table)
        //{
        //    string prefix = "@ut";
        //    prefix = "";

        //   // return System.Text.Encoding.UTF8.GetBytes(prefix + table);
        //    return System.Text.Encoding.ASCII.GetBytes(prefix + table);
        //}

        //private string GTB(byte[] table)
        //{
        //    return System.Text.Encoding.UTF8.GetString(table);
        //}

        public void TEST_REMOVE()
        {
            File.Delete(@"D:\temp\DBreezeTest\fdbLtrie.rhp");
            File.Delete(@"D:\temp\DBreezeTest\fdbLtrie.rol");
            File.Delete(@"D:\temp\DBreezeTest\fdbLtrie.txt");

            InitLTrieAscii();



            byte[] p1 = new byte[] { 1, 1, 1 };
            byte[] p2 = new byte[] { 1, 1, 2 };
            byte[] p3 = new byte[] { 1, 2 };

            LTrie.Add(p1, null);
            LTrie.Commit();
            LTrie.Add(p2, null);
            LTrie.Commit();
            LTrie.Add(p3, null);
            LTrie.Commit();

            Console.WriteLine("CNT: {0}", LTrie.Count(false));
            foreach (var r in LTrie.IterateForward())
            {
                Console.WriteLine("K: {0}", r.Key.ToBytesString(""));
            }


            LTrie.Remove(ref p1);
            LTrie.Commit();
            LTrie.Remove(ref p2);
            LTrie.Commit();

            Console.WriteLine("************************");

            Console.WriteLine("CNT: {0}", LTrie.Count(false));
            foreach (var r in LTrie.IterateForward())
            {
                Console.WriteLine("K: {0}", r.Key.ToBytesString(""));
            }


            LTrie.Add(p1, null);
            LTrie.Commit();
            LTrie.Add(p2, null);
            LTrie.Commit();

            Console.WriteLine("************************");

            Console.WriteLine("CNT: {0}", LTrie.Count(false));
            foreach (var r in LTrie.IterateForward())
            {
                Console.WriteLine("K: {0}", r.Key.ToBytesString(""));
            }

            return;



            byte[] t1 = new byte[] { 1 };
            byte[] t2 = new byte[] { 2 };


            LTrie.Add(t1, null);
            LTrie.Commit();


            Console.WriteLine("CNT: {0}", LTrie.Count(false));
            foreach (var r in LTrie.IterateForward())
            {
                Console.WriteLine("K: {0}", r.Key.ToBytesString(""));
            }

            Console.WriteLine("************************");

            byte[] k2r = t1;
            LTrie.Remove(ref k2r);
            LTrie.Commit();


            Console.WriteLine("CNT: {0}", LTrie.Count(false));
            foreach (var r in LTrie.IterateForward())
            {
                Console.WriteLine("K: {0}", r.Key.ToBytesString(""));
            }

            Console.WriteLine("************************");

            // Console.WriteLine(GT(t1).ToBytesString(""));
            // Console.WriteLine(GT(t2).ToBytesString(""));

            LTrie.Add(t2, null);
            LTrie.Commit();



            Console.WriteLine("CNT: {0}", LTrie.Count(false));
            foreach (var r in LTrie.IterateForward())
            {
                Console.WriteLine("K: {0}", r.Key.ToBytesString(""));
            }
        }

        //public void TEST_REMOVE()
        //{
        //    File.Delete(@"D:\temp\DBreezeTest\fdbLtrie.rhp");
        //    File.Delete(@"D:\temp\DBreezeTest\fdbLtrie.rol");
        //    File.Delete(@"D:\temp\DBreezeTest\fdbLtrie.txt");

        //    InitLTrieAscii();

        //    string t1 = "t1";
        //    string t2 = "t2";
        //    t1 = "1";
        //    t2 = "2";


        //    LTrie.Add(GT(t1), null);
        //    LTrie.Commit();
        //    LTrie.Add(GT(t2), null);
        //    LTrie.Commit();

        //    Console.WriteLine("CNT: {0}", LTrie.Count());
        //    foreach (var r in LTrie.IterateForward())
        //    {
        //        Console.WriteLine("K: {0}", GTB(r.Key));
        //    }

        //    Console.WriteLine("************************");

        //    byte[] k2r = GT(t1);
        //    LTrie.Remove(ref k2r);
        //    LTrie.Commit();
        //    k2r = GT(t2);
        //    LTrie.Remove(ref k2r);
        //    LTrie.Commit();

        //    Console.WriteLine("CNT: {0}", LTrie.Count());
        //    foreach (var r in LTrie.IterateForward())
        //    {
        //        Console.WriteLine("K: {0}", GTB(r.Key));
        //    }

        //    Console.WriteLine("************************");

        //   // Console.WriteLine(GT(t1).ToBytesString(""));
        //   // Console.WriteLine(GT(t2).ToBytesString(""));

        //    LTrie.Add(GT(t1), null);
        //    LTrie.Commit();
        //    LTrie.Add(GT(t2), null);
        //    LTrie.Commit();
                        

        //    Console.WriteLine("CNT: {0}", LTrie.Count());
        //    foreach (var r in LTrie.IterateForward())
        //    {
        //        Console.WriteLine("K: {0}", GTB(r.Key));
        //    }
        //}


        /// <summary>
        /// 
        /// </summary>
        public void RUN_LTrie_MainTests()
        {
            InitLTrieAscii();
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            #region "STANDARD TESTS"

            byte[] testKey = null;

            /*TEST 1*/
            //DateTime dt = new DateTime(1970, 1, 1);
            //DateTime dt = DateTime.Now;
            ////dt = new DateTime(2200, 1, 1);
            //for (int i = 0; i < 1000000; i++)
            //{
            //    testKey = dt.Ticks.To_8_bytes_array_BigEndian();
            //    LTrie.Add(testKey, new byte[] { 2 });
            //    //dt = dt.AddHours(1);
            //    dt = dt.AddSeconds(7);

            //    //LTrie.Commit();
            //}
            ////Console.WriteLine(dt.ToString());
            //LTrie.Commit();
            /***********************/

            DBreeze.Diagnostic.SpeedStatistic.PrintOut();

            /*TEST 2*/
            //Random rnd = new Random();
            //int vvv = 0;
            ////rndInsertTest2Keys.Clear();
            ////dRndInsertTest2Keys.Clear();
            //for (int i = 0; i < 1000000; i++)
            //{
            //    vvv = rnd.Next(Int32.MaxValue);
            //    testKey = vvv.To_4_bytes_array_BigEndian();

            //    //rndInsertTest2Keys.Add(testKey);
            //    //dRndInsertTest2Keys.Add(testKey, testKey.ToBytesString(""));

            //    LTrie.Add(testKey, new byte[] { 1 });

            //    //LTrie.Commit();
            //}

            //LTrie.Commit();
            /***********************/

            /*TEST 3*/
            //for (int i = 0; i < 1000000; i++)
            //{
            //    testKey = i.To_4_bytes_array_BigEndian();
            //    //if (testKey._ByteArrayEquals(new byte[] { 0, 0, 0xFF, 0 }))
            //    //{
            //    //    return;
            //    //}
            //    LTrie.Add(testKey, new byte[] { 1 });
            //    //LTrie.Commit();
            //}

            //LTrie.Commit();
            /***********************/            

            #endregion


            #region "test inserts"
            
           // decimal d= -6546546845.4564456465465465468465468M;
           // double d1=0;
           // d1 = -6546546845.4564688768765;


           // System.Data.SqlTypes.SqlDecimal s = new System.Data.SqlTypes.SqlDecimal(d);

           // //System.Data.SqlTypes.SqlDouble sd = new System.Data.SqlTypes.SqlDouble(d1);

           //// ShowI(150000M);
           // ShowI(1587987970000M);
           // ShowI(-1587987970000M);
           // ShowI(1587987970000.4564645M);
           // ShowI(150000.4564645M);
           // ShowI(150000M);
           // ShowI(10000.123213M);
           // ShowI(10000.113213M);            
           // ShowI(10000M);
           // ShowI(1M);
           // ShowI(0M);
           // ShowI(-1M);
           // ShowI(-10000M);
           // ShowI(-10000.123213M);

           // return;
            //byte[] bt = null;
            //ulong ul = 64654684645465465;
            //byte b = 0;
            //double d=0;
            //d = -6546546845.4564688768765e+154;
            //double d1 = 0;
            //string ds = "-6546546845.4564688768765e+154";
            //double dsd = 0;
            //Double.TryParse(ds, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dsd);

            //for (int i = 0; i < 1000000; i++)
            //{
            //     //bt = System.BitConverter.GetBytes(-6546546845.4564688768765e+154);
            //    //bt = NumberConversions.DoubleToByteArray(-6546546845.4564688768765e+154);
               
            //    //d1 = (long)d;

                
                
            //    for (int x = 0; x < 16; x++)
            //    {

            //    }
                
            //    //d.ToString();
            //    //bt = System.BitConverter.GetBytes(1);            
            //}

            //LTrie.Add(new byte[] { 64, 64, 64, 64, 76, 97, 115, 116, 70, 105, 108, 101, 78, 117, 109, 98, 101, 114 }, new byte[] { 0, 0, 0, 0, 0, 152, 150, 129 });
            //LTrie.Add(new byte[] { 64, 117, 116, 65, 117, 116, 111, 49, 51, 50, 47, 73, 116, 101, 109, 115 }, new byte[] { 0, 1, 0, 0, 0, 0, 0, 152, 150, 129 });
            //LTrie.Commit();

            //LTrie.Add(new byte[0], new byte[1] { 1 });
            //LTrie.Remove(new byte[0]);
            //LTrie.Commit();
            //return;
            //Testing partial value insert
            //LTrie.AddPartially(new byte[] { 1 }, new byte[] { 1 }, 0);
            //LTrie.AddPartially(new byte[] { 1 }, new byte[] { 2 }, 1);
            //LTrie.AddPartially(new byte[] { 1 }, new byte[] { 9 }, 10);
            //LTrie.AddPartially(new byte[] { 1 }, new byte[] { 7 }, 5);
            //LTrie.Commit();

            //LTrie.Add(new byte[] { 12 }, GB("www"));
            //LTrie.Commit();

            //LTrie.ChangeKey(new byte[] { 12 }, new byte[] { 17 });
            //LTrie.Commit();

            //for (int i = 0; i < 100000; i++)
            //{
            //    testKey = i.To_4_bytes_array_BigEndian();
            //    //if (testKey._ByteArrayEquals(new byte[] { 0, 0, 0xFF, 0 }))
            //    //{
            //    //    return;
            //    //}
            //    LTrie.Add(testKey, new byte[] { 1 });
            //    //LTrie.Commit();
            //}

            //LTrie.Commit();


            //DateTime dt = new DateTime(1970, 1, 1);
            ////dt = new DateTime(2200, 1, 1);
            //for (int i = 0; i < 100; i++)
            //{
            //    testKey = dt.Ticks.To_8_bytes_array_BigEndian();
            //    LTrie.Add(testKey, GB("1"));
            //    dt = dt.AddHours(1);
            //    //dt = dt.AddSeconds(7);

            //    //LTrie.Commit();
            //}
            ////Console.WriteLine(dt.ToString());
            //LTrie.Commit();



            //////////////////////////////////////////////////////////////////////  GOOD TEST
            //Random rnd = new Random();
            //int vvv = 0;
            ////rndInsertTest2Keys.Clear();
            ////dRndInsertTest2Keys.Clear();
            //Dictionary<string, byte> _frt = new Dictionary<string, byte>();
            //string hash = String.Empty;
            
            //for (int i = 0; i < 100000; i++)
            //{
            //    vvv = rnd.Next(Int32.MaxValue);
            //    testKey = vvv.To_4_bytes_array_BigEndian();

            //    hash = testKey.ToBytesString();
            //    if (!_frt.ContainsKey(hash))
            //    {
            //        _frt.Add(hash, 1);
            //    }
                

            //    //rndInsertTest2Keys.Add(testKey);
            //    //dRndInsertTest2Keys.Add(testKey, testKey.ToBytesString(""));

            //    LTrie.Add(testKey, new byte[] { 1 });

            //    //LTrie.Commit();
            //}

            //LTrie.Commit();
            //Console.WriteLine("FRT Count {0}", _frt.Count());
            //////////////////////////////////////////////////////////////////////  EO GOOD TEST



            //LTrie.Add(GB("w"), GB("Well done my young padavan!"));
            //LTrie.Add(GB(""), GB("EMPTY Well done my young padavan!"));
            //LTrie.Add(GB("ww"), GB("Good weather!"));
            //LTrie.Add(GB("wa"), GB("Good weather!"));
            //LTrie.Add(GB("wb"), GB("Good weather!"));
            //LTrie.Add(GB("wc"), GB("Good weather!"));
            //LTrie.Add(GB("www"), GB("Good weather!"));
            //LTrie.Add(GB("wwa"), GB("Good weather!"));
            //LTrie.Add(GB("wwb"), GB("Good weather!"));
            //LTrie.Add(GB("wwc"), GB("Good weather!"));
            //LTrie.Add(GB("wwd"), GB("Good weather!"));
            //LTrie.Add(GB("wwww"), GB("Smooth!"));
            //LTrie.Add(GB("wwwa"), GB("Smooth!"));
            //LTrie.Add(GB("wwwc"), GB("Smooth!"));
            //LTrie.Add(GB("wwwd"), GB("Smooth!"));
            //LTrie.Add(GB("a"), GB("Good weather!"));
            //LTrie.Add(GB("aa"), GB("Smooth!"));
            //LTrie.Add(GB("z"), GB("Good weather!"));
            //LTrie.Add(GB("zzz"), GB("Smooth!"));
            //LTrie.Add(GB("zz"), GB("Smooth!"));
            //////LTrie.Add(GB("ww"), GB("Take!"));
            //////LTrie.Add(GB("w"), GB("done my young padavan!"));
            //////LTrie.Add(GB("ww"), GB("weather!"));
            //////LTrie.Add(GB("wwww"), GB("mooth!"));
            //////LTrie.Add(GB("ww"), GB("ake!"));

            //LTrie.Commit();

            //LTrie.Add(GB("wsdfsdfsdfsdfsdf"), GB("Well done my young padavan!"));

            //LTrie.RemoveAll(true);

            //LTrie.Add(GB("aaaaaaasdfsdf"), GB("Well done my young padavan!"));
            //LTrie.Commit();

            //LTrie.Add(GB("w"), GB("Well done my young padavan!!!"));
            //LTrie.RollBack();

            //LTrie.Add(GB("w"), GB("Well"));
            //LTrie.Commit();

            //LTrie.Add(GB("a"), GB("Well deon"));

            //var row = LTrie.GetKey(GB("w"));
            ////Console.WriteLine("Exists: {2}; K: {0}; V: {1}", row.Key.ToBytesString(""), row.GetFullValue().ToBytesString(""),row.Exists);
            //Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), (row.Exists) ? GS(row.GetFullValue()) : "doesn't exist");
            //LTrie.Commit();
            //row = LTrie.GetKey(GB("w"));
            //Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), (row.Exists) ? GS(row.GetFullValue()) : "doesn't exist");
            //row = LTrie.GetKey(GB("a"));
            //Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), (row.Exists) ? GS(row.GetFullValue()) : "doesn't exist");
            //Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.GetFullValue().ToBytesString(""));

            //Random rnd = new Random();
            //int vvv = 0;
            //List<byte[]> ttt = new List<byte[]>();
            //for (int i = 0; i < 1000; i++)
            //{
            //    vvv = rnd.Next(100000);
            //    testKey = vvv.To_4_bytes_array_BigEndian();
            //    //rndInsertTest2Keys.Add(testKey);
            //    //dRndInsertTest2Keys.Add(testKey, testKey.ToBytesString(""));
            //    ttt.Add(testKey);
            //    Console.WriteLine("INSERTING KEY: {0}", testKey.ToBytesString(""));
            //    LTrie.Add(testKey, new byte[] { 1 });

            //    //LTrie.Commit();
            //}

            //LTrieRow row = null;
            //int xxx = 0;
            //foreach (var bt in ttt)
            //{
            //    row = LTrie.GetKey(bt);
            //    if (row.Exists)
            //    {
            //        Console.WriteLine("Val: {0}", row.GetFullValue().ToBytesString(""));
            //    }
            //}

            //LTrie.Commit();

            //xxx = 0;
            //foreach (var bt in ttt)
            //{
            //    row = LTrie.GetKey(bt);
            //    if (row.Exists)
            //    {
            //        Console.WriteLine("READING KEY: {0}; Val: {1}", row.Key.ToBytesString(""), row.GetFullValue().ToBytesString(""));
            //        xxx++;
            //    }
            //}

            //Console.WriteLine("XXX {0}", xxx);
            //return;

            //LTrie.Add(GB("w"), G_SV200_1);
            //LTrie.Add(GB("w"), G_SV2);
            //LTrie.Add(GB("a"), G_SV2);
            //LTrie.Add(GB("b"), G_SV2);
            //LTrie.Add(GB("c"), G_SV2);
            //LTrie.Add(GB("d"), G_SV2);

            //LTrie.Add(GB("w"), G_SV1);
            //LTrie.Add(GB("www"), G_SV2);
            //LTrie.Add(GB("ww"), G_SV22);
            ////LTrie.Add(GB("w"), G_SV1);

            ////LTrie.Add(GB("a"), null);
            ////LTrie.Add(GB("w"), G_SV2);
            ////LTrie.Add(GB("www"), G_SV2);
            ////LTrie.Add(GB("ww"), G_SV2);
            ////LTrie.Add(GB("w"), G_SV2);

            ////LTrie.Add(GB("w"), G_SV200_1);
            ////LTrie.Add(GB("www"), G_SV200_1);
            ////LTrie.Add(GB("ww"), G_SV200_1);
            //////LTrie.Add(GB("w"), G_SV200_1);

            //LTrie.Commit();


            #endregion



            sw.Stop();
            Console.WriteLine("INSERT IS DONE " + sw.ElapsedMilliseconds.ToString());
        }

        #region "Help transformation functions"

        private byte[] GB(string str)
        {
            return System.Text.Encoding.ASCII.GetBytes(str);
        }

        private string GS(byte[] bt)
        {
            return System.Text.Encoding.ASCII.GetString(bt);
        }

        private byte[] G_SV1
        {
            get
            {
                //Get simple value
                return new byte[] { 1 };
            }
        }

        private byte[] G_SV2
        {
            get
            {
                //Get simple value
                return new byte[] { 2 };
            }
        }

        private byte[] G_SV22
        {
            get
            {
                //Get simple value
                return new byte[] { 2,2 };
            }
        }

        private byte[] G_SV200_1
        {
            get
            {
                //Get simple value
                byte[] bt = new byte[200];
                for (int i = 0; i < bt.Length; i++)
                {
                    bt[i] = 1;
                }

                return bt;
            }
        }

        private byte[] G_SV196_1
        {
            get
            {
                //Get simple value
                byte[] bt = new byte[196];
                for (int i = 0; i < bt.Length; i++)
                {
                    bt[i] = 1;
                }

                return bt;
            }
        }

        #endregion


        public void RUN_FETCH()
        {
            InitLTrieAscii();

          

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            //Console.WriteLine("TotalRecordsCount: {0}", LTrie.Count());

            //var row = LTrie.GetKey(new byte[0]);
            //if (row.Exists)
            //{
            //    byte[] nn = row.GetFullValue();
            //}

            //row = LTrie.IterateBackwardForMaximal();
            //if (row.Exists)
            //    Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.GetFullValue().ToBytesString(""));
            //return;

            //MIN
            //var row = LTrie.IterateForwardForMinimal();
            //if (row.Exists)
            //{
            //    Console.WriteLine("MIN K: {0}; V: {1}", new DateTime(row.Key.To_Int64_BigEndian()).ToString("dd.MM.yyyy HH:mm:ss"), row.GetFullValue().ToBytesString(""));
            //}
            //else
            //{
            //    Console.WriteLine("doesn't exists");
            //}

            //MAX
            //var row = LTrie.IterateBackwardForMaximal();
            //if (row.Exists)
            //{
            //    Console.WriteLine("MAX K: {0}; V: {1}", new DateTime(row.Key.To_Int64_BigEndian()).ToString("dd.MM.yyyy HH:mm:ss"), row.GetFullValue().ToBytesString(""));
            //}
            //else
            //{
            //    Console.WriteLine("doesn't exists");
            //}


            //var row = LTrie.GetKey(GB("w"));
            //Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.GetFullValue().ToBytesString(""));
            //row = LTrie.GetKey(GB("www"));
            //Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.GetFullValue().ToBytesString(""));
            //row = LTrie.GetKey(GB("ww"));
            //Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.GetFullValue().ToBytesString(""));
            ////byte[] fdF= row.GetFullValue();
            //return;

            //SKIP



            //Console.WriteLine("***************************************************");
            //byte[] btSkipKey = ((int)10000).To_4_bytes_array_BigEndian();
            //byte[] btEndKey = ((int)10004).To_4_bytes_array_BigEndian();

            ////foreach (var tkv in LTrie.IterateForwardFromTo(btSkipKey, btEndKey,false,false).Take(5))
            //foreach (var tkv in LTrie.IterateBackwardFromTo(btEndKey, btSkipKey, false, false).Take(5))
            ////foreach (var tkv in LTrie.IterateBackwardSkipFrom(btSkipKey, 1).Take(5))
            ////foreach (var tkv in LTrie.IterateForwardSkipFrom(btSkipKey,1).Take(3))
            ////foreach (var tkv in LTrie.IterateBackwardSkip(1).Take(3))
            ////foreach (var tkv in LTrie.IterateForward().Take(20))
            //{
            //    if (tkv.Exists)
            //    {
            //        Console.WriteLine("K: {0}; V: {1}", tkv.Key.To_Int32_BigEndian(), tkv.GetFullValue().ToBytesString(""));
            //    }
            //    else
            //    {
            //        Console.WriteLine("doesn't exists");
            //    }
            //}
            //Console.WriteLine("***************************************************");
            //return;


            //Console.WriteLine("***************************************************");
            
            ////foreach (var tkv in LTrie.IterateBackwardStartFrom(GB("wwww"),true))
            //foreach (var tkv in LTrie.IterateBackwardStartsWith(GB("aa")))
            ////foreach (var tkv in LTrie.IterateForwardStartsWith(GB("w2"))) 
            //{
            //    if (tkv.Exists)
            //    {
            //        Console.WriteLine("K: {0}; V: {1}", GS(tkv.Key), (tkv.Exists) ? GS(tkv.GetFullValue()) : "doesn't exist");
            //    }
            //    else
            //    {
            //        Console.WriteLine("doesn't exists");
            //    }
            //}
            //Console.WriteLine("***************************************************");
            //return;

            //Console.WriteLine("***************************************************");
            //byte[] btKey1 = null;
            //Random rnd = new Random();
            //for (int iTrt = 0; iTrt < 100000; iTrt++)
            //{
            //    btKey1 = rnd.Next(999999).To_4_bytes_array_BigEndian();
            //    LTrie.GetKey(btKey1);
            //}
            //Console.WriteLine("***************************************************");


            Console.WriteLine("***************************************************");
            int i = 0;

            //DateTime dtTest = new DateTime(1970, 2, 15, 12, 0, 0);
            //byte[] btTestTicks = dtTest.Ticks.To_8_bytes_array_BigEndian();

            ////foreach (var tkv in LTrie.IterateBackwardStartFrom(btTestTicks).Take(15))
            ////foreach (var tkv in LTrie.IterateForwardStartFrom(btTestTicks).Take(17))
            ////foreach (var tkv in LTrie.IterateBackwardStartFrom(GB("zzz")).Take(15))
            ////foreach (var tkv in LTrie.IterateForwardStartFrom(new byte[] {0,0,0,10}).Take(15))
            ////foreach (var tkv in LTrie.IterateForwardStartFrom(GB("a")).Take(15))
            ////foreach (var tkv in LTrie.IterateBackward().Take(15))
            foreach (var tkv in LTrie.IterateBackward().Take(10000))
            ////foreach (var tkv in LTrie.IterateForward().Take(27))
            //foreach (var tkv in LTrie.IterateForwardStartsWith(GB("wwww")))
            //foreach (var tkv in LTrie.IterateForward())  //5.9 sec 1 mln TEST1
            //foreach (var tkv in LTrie.GetForward())   //12 sec 1 mln TEST1
            ////foreach (var tkv in LTrie.GetBackward())
            {
            //    if (tkv.Exists)
            //    {
            //        //tkv.GetFullValue();

                    //Console.WriteLine("K: {0}; V: {1}", tkv.Key.ToBytesString(""), (tkv.Exists) ? GS(tkv.GetFullValue()) : "doesn't exist");
            //Console.WriteLine("K: {0}; V: {1}", GS(tkv.Key), (tkv.Exists) ? GS(tkv.GetFullValue(true)) : "doesn't exist");
            //        //Console.WriteLine("K: {0}; V: {1}", new DateTime(tkv.Key.To_Int64_BigEndian()).ToString("dd.MM.yyyy HH:mm:ss"), tkv.GetFullValue().ToBytesString(""));
            //        //tkv.GetFullValue();
            //        //if (GS(tkv.GetFullValue()) == "2")
            //        //{

            //        //}

            //        //if (tkv.GetFullValue().ToBytesString("").Equals("02"))
            //        //{

            //        //}
            //        ////Console.WriteLine("K: {0}; V: {1}", tkv.Key.ToBytesStringDec(""), tkv.GetFullValue().ToBytesStringDec(""));
            //        //byte[] b = tkv.GetFullValue();
             Console.WriteLine("K: {0}; V: {1}", tkv.Key.ToBytesString(""), tkv.GetFullValue(true).ToBytesString(""));
            //        //Console.WriteLine("K: {0}; V: {1}; KeyAsAscii: {2}", tkv.Key.ToBytesString(""), tkv.GetFullValue().ToBytesString(""), GS(tkv.Key));
            //    }
            //    else
            //    {
            //        Console.WriteLine("doesn't exists");
            //    }


                i++;
            }

            Console.WriteLine("Forward Count: {0}", i);
            Console.WriteLine("***************************************************");

            sw.Stop();
            Console.WriteLine("FETCH IS DONE " + sw.ElapsedMilliseconds.ToString());


        }

        #endregion





        #region "Testing DBreeze"

        DBreezeEngine engine = null;
        object lockInitDb = new object();

        private void InitDb()
        {
            lock(lockInitDb)
            {
                if(engine == null)
                    engine = new DBreezeEngine(@"D:\temp\DBreezeTest\DBR1");
            }
        }

        public void StartDBreeze()
        {
            //Safe Db initializer
            InitDb();

            return;

            //using (var tran = engine.GetTransaction())
            //{
            //    tran.Insert("t1", new byte[] { 1 }, new byte[] { 1 });
            //    tran.Commit();
            //}
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            using (var tran = engine.GetTransaction())
            {

                #region "STANDARD TESTS"
                ////TEST 1
                DateTime dt = new DateTime(1970, 1, 1);
                //dt = new DateTime(2200, 1, 1);
                byte[] testKey = null;
                for (int i = 0; i < 1000000; i++)
                {
                    testKey = dt.Ticks.To_8_bytes_array_BigEndian();
                    //LTrie.Add(testKey, new byte[] { 2 });
                    tran.Insert("t1", testKey, new byte[] { 2 });
                    dt = dt.AddHours(1);
                    //dt = dt.AddSeconds(7);

                    //LTrie.Commit();
                }
                //Console.WriteLine(dt.ToString());
                tran.Commit();
                #endregion

                #region "Testing Partial Value Read"
                //tran.Insert("t1", GB("aaa"), new byte[] {1,2,3,4,5,6,7,8,9});
                //tran.Commit();

                //var row = tran.Select("t1", GB("aaa"));
                //if (row.Exists)
                //{
                //    //Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.GetFullValue().ToBytesString(""));
                //    Console.WriteLine("FULL K: {0}; V: {1}", GS(row.Key), row.GetFullValue().ToBytesString(""));
                //    byte[] partial = row.GetPartialValue(7, 1);
                //}
                //else
                //{
                //    Console.WriteLine("K: {0} doesn't exist", row.Key.ToBytesString(""));
                //}
                #endregion

                

                //tran.Insert("t1", GB("aaa"), GB("oooo"));
                //tran.Insert("t2", GB("aaa"), GB("oooo"));
                //tran.Commit();

                //var row = tran.Select("t1", GB("aaa"));
                //if (row.Exists)
                //{
                //    //Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.GetFullValue().ToBytesString(""));
                //    Console.WriteLine("K: {0}; V: {1}", GS(row.Key), GS(row.GetFullValue()));
                //}
                //else
                //{
                //    Console.WriteLine("K: {0} doesn't exist", row.Key.ToBytesString(""));
                //}

                //row = tran.Select("t2", GB("aaa"));
                //if (row.Exists)
                //{
                //    //Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.GetFullValue().ToBytesString(""));
                //    Console.WriteLine("K: {0}; V: {1}", GS(row.Key), GS(row.GetFullValue()));
                //}
                //else
                //{
                //    Console.WriteLine("K: {0} doesn't exist", row.Key.ToBytesString(""));
                //}
            }

            sw.Stop();
            Console.WriteLine("TRAN INSERT IS DONE " + sw.ElapsedMilliseconds.ToString());
        }


        //enum enumss:ushort
        //{
        //    sdf,
        //    df,
        //    dsf
        //}

        void enumtest()
        {
            //using (var tran = engine.GetTransaction())
            //{
            //    tran.Insert<enumss, int>("t1", enumss.df, 324);
            //    tran.Commit();
            //}

            //using (var tran = engine.GetTransaction())
            //{
            //    var row = tran.Select<enumss, int>("t1", enumss.df);
            //    Console.WriteLine(row.Value);
            //}

            var gd = Guid.NewGuid();

            using (var tran = engine.GetTransaction())
            {
               
                tran.Insert<Guid, int>("t1", gd, 324);
                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {
                var row = tran.Select<Guid, int>("t1", gd);
                Console.WriteLine(row.Value);
            }
        }

        /// <summary>
        /// With backup
        /// </summary>
        public void StartTest()
        {
           
            lock (lockInitDb)
            {
                if (engine == null)
                {

                    DBreezeConfiguration conf = new DBreezeConfiguration()
                    {
                        DBreezeDataFolderName = @"D:\temp\DBreezeTest\DBR1",
                        // DBreezeDataFolderName = @"E:\temp\DBreezeTest\DBR1",                        
                        // DBreezeDataFolderName = @"C:\tmp",
                        Storage = DBreezeConfiguration.eStorage.DISK,
                        // Storage = DBreezeConfiguration.eStorage.MEMORY,
                        //Backup = new Backup()
                        //{
                        //    BackupFolderName = @"D:\temp\DBreezeTest\DBR1\Bup",
                        //    IncrementalBackupFileIntervalMin = 30
                        //}
                    };

                    //conf.AlternativeTablesLocations.Add("t11",@"D:\temp\DBreezeTest\DBR1\INT");
                    //conf.AlternativeTablesLocations.Add("mem_*", String.Empty);
                    //conf.AlternativeTablesLocations.Add("t2", @"D:\temp\DBreezeTest\DBR1\INT");
                    //conf.AlternativeTablesLocations.Add("t*", @"D:\temp\DBreezeTest\DBR1\INT");

                    engine = new DBreezeEngine(conf);

                }
            }


            //  enumtest();

            //MATRIX_BUILD();
            // MATRIX_READOUT_V2();

            // TestSecondaryIndexPreparation();
            //TestNewStorageLayer();

            // TestMemoryStorage();
            //TestClosestToPrefix();

            // testC6();
            //testC7();
            //testC8();
            //testC9();
            //testC10();

            //testF_004();
            // testF_003();
            //testF_009();

            // testF_010();

            // testF_001();
            //testC14();
            //testC15();
            //testC16();

            //TestKrome();
            //testF_002();

            //TestSFI1();
            //TestSFI2();

            //TestMixedStorageMode();

            //TestIterators();
            //TestIteratorsv11();

            //testbackwardwithStrings();

            //TestBackUp();
            //TestSelectBackwardStartWith_WRITE();
            //TestSelectBackwardStartWith_READ();

            //TR();


            //NetworkTest();
            //NetworkTestDisk();
            Test_valueIsUsed();
        }

        void Test_valueIsUsed()
        {
            //using (var tran = engine.GetTransaction())
            //{
            //    tran.Insert<int, int>("a", 1, 1);
            //    tran.Commit();
            //}

            using (var tran = engine.GetTransaction())
            {
               // tran.ValuesLazyLoadingIsOn = false;
                var row = tran.Select<int, int>("a", 1);
                var v = row.Value;

                //tran.Insert<int, int>("a", 1, 2);

                v = row.Value;

                //foreach (var xrow in tran.SelectForward<int, int>("a"))
                //{
                //    var v1 = xrow.Value;
                //    v1 = xrow.Value;
                //}


                //tran.Commit();
            }
        }


        public class RemoteInstanceCommunicator : DBreeze.Storage.RemoteInstance.IRemoteInstanceCommunicator
        {
            DBreeze.Storage.RemoteInstance.RemoteTablesHandler rth = new DBreeze.Storage.RemoteInstance.RemoteTablesHandler(@"D:\temp\DBreezeTest");

            public byte[] Send(byte[] data)
            {
                return rth.ParseProtocol(data);
            }
        }

        DBreezeRemoteEngine remoteEngine = null;

        // <summary>
        /// 
        /// </summary>
        public void NetworkTest()
        {
            if (remoteEngine == null)
            {

                DBreezeConfiguration conf = new DBreezeConfiguration()
                {
                   // DBreezeDataFolderName = @"D:\temp\DBreezeTest\DBR1",
                    DBreezeDataFolderName = @"TestPuppy", //Here must be folder name in protoected database, later we have to take care alternative locations.
                    Storage = DBreezeConfiguration.eStorage.RemoteInstance,                    
                };

                conf.RICommunicator = new RemoteInstanceCommunicator();
                remoteEngine = new DBreezeRemoteEngine(conf);

            }




            //using (var tran = remoteEngine.GetTransaction())
            //{
            //    for (int i = 1; i < 100000; i++)
            //        tran.Insert<int, string>("t1", i, "ds" + i);
            //    tran.Commit();
            //}


            using (var tran = remoteEngine.GetTransaction())
            {
                Console.WriteLine(tran.Count("t1"));
                var row = tran.Select<int, string>("t1", 700);
                if (row.Exists)
                    Console.WriteLine(row.Value);
            }
        }




        public void NetworkTestDisk()
        {
            if (engine == null)
            {

                DBreezeConfiguration conf = new DBreezeConfiguration()
                {
                    DBreezeDataFolderName = @"D:\temp\DBreezeTest\TestPuppy",                    
                    Storage = DBreezeConfiguration.eStorage.DISK,
                };

                engine = new DBreezeEngine(conf);

            }


            //using (var tran = engine.GetTransaction())
            //{
            //    tran.Insert<int, string>("t1", 1, "dsfsfsdfsdfsdf");
            //    tran.Commit();
            //}


            using (var tran = engine.GetTransaction())
            {
                Console.WriteLine(tran.Count("t1"));
                var row = tran.Select<int, string>("t1", 1);
                if (row.Exists)
                    Console.WriteLine(row.Value);
            }
        }











        public void TR()
        {
            string path = @"D:\temp\DBreezeTest\DBR1";

            for (var ctr = 1; ctr < 3; ctr++)
            {
                var databasePath = Path.Combine(path, ctr.ToString());        
                this.UseValues(databasePath, 1, 1);
            }
        }


        private void UseValues(string databasePath, int key, int val)
        {

            using (var engine = new DBreeze.DBreezeEngine(databasePath))
            {
                using (var transaction = engine.GetTransaction())
                {
                    //var row = transaction.Select<int, int>("t", key);

                    transaction.Insert("t", key, val);

                    transaction.Commit();
                }
            }

            using (var engine = new DBreeze.DBreezeEngine(databasePath))
            {
                using (var transaction = engine.GetTransaction())
                {
                    var row = transaction.Select<int, int>("t", key);

                    if (!row.Exists)
                    {
                        Console.WriteLine("Stored value incorrect");
                    }
                }
            }
        }

        //public void TR()
        //{
        //    string path = @"D:\temp\DBreezeTest\DBR1";

        //    for (var ctr = 0; ctr < 10; ctr++)
        //    {
        //        var databasePath = Path.Combine(path, ctr.ToString());
        //        this.UseDatabase(databasePath);
        //    }
        //}

        //private void UseDatabase(string databasePath)
        //{
        //    Console.WriteLine("Testing with database at {0}", databasePath);

        //    string oldVal = null;
        //    for (var ctr = 0; ctr < 5; ctr++)
        //    {
        //        var val = string.Format("xxx{0}xxx", ctr);

        //        this.UseValues(databasePath, 555, val, oldVal);

        //        oldVal = val;
        //    }

        //    Console.WriteLine("============================================");
        //}


        //private void UseValues(string databasePath, int key, string val, string oldVal)
        //{
        //    //var actualKey = key;
        //    var actualKey = BitConverter.GetBytes(key);

        //    //var actualVal = val;
        //    var actualVal = Encoding.UTF8.GetBytes(val);

        //    using (var engine = new DBreeze.DBreezeEngine(databasePath))
        //    {
        //        using (var transaction = engine.GetTransaction())
        //        {
        //            var row = transaction.Select<byte[], byte[]>("t", actualKey);

        //            string storedValue = null;
        //            if (row.Exists)
        //            {
        //                //storedValue = row.Value;
        //                storedValue = Encoding.UTF8.GetString(row.Value);
        //            }

        //            if (storedValue == oldVal)
        //                Console.Write("Old value correct, {0}; ", oldVal);
        //            else
        //                Console.Write("Old value incorrect, expected {0}, got {1}; ", oldVal, storedValue);

        //            transaction.Insert("t", actualKey, actualVal);

        //            transaction.Commit();
        //        }
        //    }

        //    using (var engine = new DBreeze.DBreezeEngine(databasePath))
        //    {
        //        using (var transaction = engine.GetTransaction())
        //        {
        //            var row = transaction.Select<byte[], byte[]>("t", actualKey);

        //            string storedValue = null;
        //            if (row.Exists)
        //            {
        //                //storedValue = row.Value;
        //                storedValue = Encoding.UTF8.GetString(row.Value);
        //            }

        //            if (storedValue == val)
        //                Console.WriteLine("Stored value correct, {0}", val);
        //            else
        //                Console.WriteLine("Stored value incorrect, expected {0}, got {1}", val, storedValue);
        //        }
        //    }
        //}



        /// <summary>
        /// Without backup
        /// </summary>
        public void StartTest2()
        {
            InitDb();

            //TestLinkToValue();
            //TestSelectDataBlock();

            //TestInsertPart1();
            //TestLinkToValue();
            //TestSelectDataBlock_MT();
            //TestSelectDataBlock();

            //TestBackUp();

           

            //this.TestRollbackV1();

            //this.testPartialUpdate();

            //testChar();

            //TEST_DBINTABLE_MT_START();
            //TEST_DBINTABLE();

            //TEST_START_WITH();

            //TEST_VIRT_ROLLBACK();

            #region "Archive calls"
            //TEST_SPIN_START();

            //TEST_TABLE_FOREACH_INSERT();

            //TEST_TABLE_RESERVED_FOR_WRITE_1();
            //TEST_TABLE_RESERVED_FOR_WRITE();

            //TEST_START_PARALLEL_ACCESS_CLOSE();


            //TEST_UPDATE_GROWS_UP();

            //TEST_Nw_RB();

            //TEST_DBR_PAR_FETCH_DELETE();

            //TEST_PAR_FETCH_DELETE();

            //TEST_INSERTING_READING_DATETIME();

            //Test_Partial_Insert();

            //Test_Strings();

            //Test_DbTypesConversion();

            //TEST_SKIP_FROM();

            //TEST_START_DEADLOCKCASE();

            //TEST_TABLES_SYNCHRONIZATION();

            //TESTREADSYNCHRO_TEST();

           // TEST_Write1MLN_DateTimeGrowing("t1", 2000);
           
            //TEST_INSERTING_READING_DATETIME();
            //TEST_INSERTING_READING_STRING();

            //TESTSCHEMA_TEST1();
            //TESTSCHEMA_TEST_DELETETABLE();
            #endregion
        }

        #region "STARTING THREADS"

        public void StartThread1()
        {
            InitDb();

            Action a = () =>
            {
                //Insert1MLN_TEST1("");
                //TEST_PAR_FETCH_DELETE_fetch();
                TEST_DBR_PAR_FETCH_DELETE_fetch(1);
                //TEST_DBR_PAR_FETCH_DELETE_fetch(1);
            };

            a.DoAsync();
        }

        public void StartThread2()
        {
            InitDb();

            Action a = () =>
            {
                //Insert1MLN_TEST1("");
                TEST_DBR_PAR_FETCH_DELETE_del();
                //TEST_PAR_FETCH_DELETE_del();
                //TEST_DBR_PAR_FETCH_DELETE_del();
                
            };

            a.DoAsync();
        }

        public void StartThread3()
        {
            InitDb();

            Action a = () =>
            {
                //Insert1MLN_TEST1("");
                TEST_DBR_PAR_FETCH_DELETE_fetch(2);
               
            };

            a.DoAsync();
        }

        #endregion


        // [ProtoBuf.ProtoContract]
        // public class XYZ
        // {
        //     public XYZ()
        //     {
        //         P1 = 12;
        //         P2 = "sdfs";
        //     }

        //     [ProtoBuf.ProtoMember(1, IsRequired = true)]
        //     public int P1 { get; set; }
        //     [ProtoBuf.ProtoMember(2, IsRequired = true)]
        //     public string P2 { get; set; }
        // }

        //private void testF_010()
        //{
        //    if (DBreeze.Utils.CustomSerializator.ByteArraySerializator == null)
        //    {
        //        DBreeze.Utils.CustomSerializator.ByteArraySerializator = WSN_DistributedApplication.Protobuf.ProtobufSerializer.SerializeProtobuf;
        //        DBreeze.Utils.CustomSerializator.ByteArrayDeSerializator = WSN_DistributedApplication.Protobuf.ProtobufSerializer.DeserializeProtobuf;
        //    }

        //    //using (var tran = engine.GetTransaction())
        //    //{

        //    //    // tran.Insert<int, DbCustomSerializer<XYZ>>("t1", 1, new XYZ());
        //    //   // tran.Insert<int, XYZ>("t1", 1, new XYZ() { P1 = 44, P2 = "well" });
        //    //    tran.Insert<int, XYZ>("t1", 1, new XYZ() { P1 = 44, P2 = "well" });
        //    //    tran.Commit();
        //    //}


        //    Type t1 = typeof(DBreeze.DataTypes.DbUTF8);
        //    Type t2 = typeof(String);
        //    Type t3 = typeof(float?);
        //    Type t4 = typeof(int);
        //    Type t5 = typeof(XYZ);
        //    Type t6 = typeof(Guid);
        //    Type t7 = typeof(string);
        //    Type t8 = typeof(Int32);

        //    using (var tran = engine.GetTransaction())
        //    {
        //        tran.Select<float?, string>("t1", 1f);

        //        //var row = tran.Select<int, XYZ>("t1", 1);
        //        //if (row.Exists)
        //        //{
        //        //    var tr = row.Value;
        //        //}
        //    }
        //}


        private void testF_009()
        {
            byte[] ptr = null;

            //using (var tran = engine.GetTransaction())
            //{
            //    //tran.Insert<int, int>("A", 256, 458, out ptr);
            //    //tran.Commit();    

            //    for (int i = 0; i < 100000; i++)
            //    {
            //        tran.Insert<int, byte[]>("A", i, new byte[2000]);
            //    }
            //    tran.Commit();
            //}


            using (var tran = engine.GetTransaction())
            {
                tran.ValuesLazyLoadingIsOn = false;

                DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
                 foreach (var row in tran.SelectForward<int, byte[]>("A"))
                 {
                     ptr = row.Value;
                     //Console.WriteLine("K: {0} - {1}", row.Key, row.Value);
                 }
                 DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);

                //var row = tran.Select<int, int>("A", 256);

                //if (row.Exists)
                //{
                //    Console.WriteLine("K: {0} - {1}", row.Key, row.Value);
                //}
            }

        }

        private void testF_008()
        {
            DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            using (var tran = engine.GetTransaction())
            {
                //tran.Technical_SetTable_OverwriteIsNotAllowed("A");
                byte[] ref2val=null;
                bool wasUpdated=false;
                for (int i = 1; i < 1000000; i++)
                {
                    tran.Insert<int, byte>("A", i, 3);
                    //tran.Insert<int, byte>("A", i, 3, out ref2val, out wasUpdated,true);
                }

                tran.Commit();
            }
            DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);

            //using (var tran = engine.GetTransaction())
            //{
            //    foreach (var row in tran.SelectForward<int, byte>("A").Take(10))
            //    {
            //        Console.WriteLine("K: {0} - {1}", row.Key,row.Value);
            //    }
            //}
        }

        private void testF_007()
        {
            using (var tran = engine.GetTransaction())
            {
                tran.Insert<byte[], byte[]>("a", new byte[] { 2 }, new byte[] { 1 });
                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {
                tran.Insert<byte[], byte[]>("a", new byte[] { 2, 3 }, new byte[] { 1 });
                tran.Insert<byte[], byte[]>("a", new byte[] { 2, 4 }, new byte[5000]);
                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {
                //tran.RemoveKey<byte[]>("a", new byte[] { 2 });
                byte[] deleted = null;
                bool WasRemoved = false;
                tran.RemoveKey<byte[]>("a", new byte[] { 2, 4 },out WasRemoved,out deleted);
                tran.Commit();
            }


            using (var tran = engine.GetTransaction())
            {
                foreach (var row in tran.SelectForward<byte[], byte[]>("a"))
                {
                    Console.WriteLine("K: {0}", row.Key.ToBytesString());
                }
            }
        }


        private void testF_007_1()
        {


            //using (var tran = engine.GetTransaction())
            //{
            //    DateTime now = DateTime.Now;
            //    DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            //    for (int i = 0; i < 100000; i++)
            //    {
            //        now = now.AddDays(2);
            //        tran.Insert<DateTime, byte[]>("A", now, new byte[200]);
            //    }

            //    tran.Commit();

            //    DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);
            //}


            //using (var tran = engine.GetTransaction())
            //{
            //    DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");

            //    int i = 0;
            //    byte[] val = null;

            //    foreach (var row in tran.SelectForward<DateTime, byte[]>("A"))
            //    {
            //        i++;
            //        val = row.Value;
            //    }

            //    DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);
            //    Console.WriteLine("Cnt: {0}", i);
            //}


        }

        private void testF_006()
        {
            //int i = 0;

            //while (i < 1000000)
            //{
            //    using (var tran = engine.GetTransaction())
            //    {
            //        tran.Insert<int, int>("t1", i, 1);
            //        tran.Insert<int, int>("t2", i, 1);
            //        i++;

            //        tran.Commit();
            //    }
            //}
        }

        private void testF_005()
        {
            DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            DBreeze.Diagnostic.SpeedStatistic.ToConsole = true;
            using (var tran = engine.GetTransaction())
            {
                for (int i = 0; i < 1000000; i++)
                {
                    tran.Insert<int, int>("t1", i, 1);
                }
                tran.Commit();
            }
            DBreeze.Diagnostic.SpeedStatistic.PrintOut("a",true);
        }


        private void testF_004()
        {
            //using (var tran = engine.GetTransaction(eDBSessionLockTypes.SHARED,"p*"))
            //{
            //    tran.Insert<int, string>("t1", 1, "Kesha is a good parrot");
            //    tran.Commit();
            //}
            List<string> sd = new List<string>();
            sd.Add("t1");
            sd.Add("t2");
            using (var tran = engine.GetTransaction(eTransactionTablesLockTypes.EXCLUSIVE,sd.ToArray()))
            {
                tran.Insert<int, string>("t1", 1, "Kesha is a good parrot");
                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {
                foreach (var r in tran.SelectForward<int, string>("t1"))
                {
                    Console.WriteLine(r.Value);
                }
            }
        }

        private void ExecF_003_1()
        {
            using (var tran = engine.GetTransaction(eTransactionTablesLockTypes.EXCLUSIVE, "t1", "p*", "c$"))
            {
                Console.WriteLine("T1 {0}> {1}; {2}", DateTime.Now.Ticks, System.Threading.Thread.CurrentThread.ManagedThreadId, DateTime.Now.ToString("HH:mm:ss.ms"));
                tran.Insert<int, string>("t1", 1, "Kesha is a good parrot");
                tran.Commit();

                Thread.Sleep(2000);
            }
        }

        private void ExecF_003_2()
        {
            List<string> tbls = new List<string>();
            tbls.Add("t1");
            tbls.Add("v2");
            using (var tran = engine.GetTransaction(eTransactionTablesLockTypes.SHARED, tbls.ToArray()))
            {
                Console.WriteLine("T2 {0}> {1}; {2}", DateTime.Now.Ticks, System.Threading.Thread.CurrentThread.ManagedThreadId, DateTime.Now.ToString("HH:mm:ss.ms"));
                foreach (var r in tran.SelectForward<int, string>("t1"))
                {
                    Console.WriteLine(r.Value);
                }              
            }
        }

        private void ExecF_003_3()
        {
            using (var tran = engine.GetTransaction(eTransactionTablesLockTypes.SHARED, "t1"))
            {
                Console.WriteLine("T3 {0}> {1}; {2}", DateTime.Now.Ticks, System.Threading.Thread.CurrentThread.ManagedThreadId, DateTime.Now.ToString("HH:mm:ss.ms"));

                //This must be used in any case, when Shared threads can have parallel writes
                tran.SynchronizeTables("t1");

                tran.Insert<int, string>("t1", 1, "Kesha is a VERY good parrot");
                tran.Commit();

                foreach (var r in tran.SelectForward<int, string>("t1"))
                {
                    Console.WriteLine(r.Value);
                }
            }
        }


        private void testF_003()
        {

            Action t2 = () =>
            {
                ExecF_003_2();
            };

            t2.DoAsync();


            Action t1 = () =>
            {
                ExecF_003_1();
            };

            t1.DoAsync();


            Action t3 = () =>
            {
                ExecF_003_3();
            };

            t3.DoAsync();
        }



        private void TestKrome()
        {

            using (var tran = engine.GetTransaction())
            {
                byte[] ptr = tran.InsertDataBlock("t1", null, new byte[] { 1, 2, 3 });

                tran.Insert<int, byte[]>("t1", 1, ptr);
                //NestedTable nt = tran.InsertTable<int>("t1", 1, 0);
                //nt.Insert<int, int>(1, 1);
                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {
                var row = tran.Select<int,byte[]>("t1",1);
                byte[] val = row.Value;
                
                //NestedTable nt = tran.SelectTable<int>("t1", 1, 0);
                //var row = nt.Select<int, int>(1);

                tran.RemoveAllKeys("t1", true);

                byte[] res = tran.SelectDataBlock("t1", val);

                //Console.WriteLine("Key: {0}", row.Value);
            }
            return;

            //using (var tran = engine.GetTransaction())
            //{
            //    for (int i = 0; i < 1000000; i++)
            //    {
            //        tran.Insert<int, byte>("t1", i, 1);
            //    }

            //    tran.Commit();
            //}

            //Console.WriteLine("***");
            //byte bt = 1;

            //using (var tran = engine.GetTransaction())
            //{
            //    DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            //    foreach (var row in tran.SelectForward<int, byte>("t1"))
            //    {
            //        bt = row.Value;
            //       // Console.WriteLine("Key: {0}", row.Key);
            //    }
            //    DBreeze.Diagnostic.SpeedStatistic.PrintOut("a",true);
            //}


            //return;

            using (var tran = engine.GetTransaction())
            {
                tran.Insert<int, byte>("t1", 1, 1);
                tran.Insert<int, byte>("t2", 1, 1);
                tran.Insert<int, byte>("t3", 1, 1);

                tran.Commit();
            }           
          

            using (var tran = engine.GetTransaction())
            {
                tran.SynchronizeTables("t2");

                LTrieSettings = new TrieSettings();
                LTrieStorage = new StorageLayer(@"E:\temp\DBreezeTest\DBR1\90000000", LTrieSettings, new DBreezeConfiguration());

                LTrie = new LTrie(LTrieStorage);
                LTrie.Add(((int)2).To_4_bytes_array_BigEndian(), ((int)2).To_4_bytes_array_BigEndian());
                LTrie.Commit();
                LTrie.Dispose();

                var row = tran.Select<int, byte>("t2", 1);

                Console.WriteLine("K: {0}", row.Value);

                tran.RestoreTableFromTheOtherFile("t2", @"E:\temp\DBreezeTest\DBR1\90000000");

                //row = tran.Select<int, byte>("t2", 1);

                Console.WriteLine("K: {0}", row.Value);
            }

            using (var tran = engine.GetTransaction())
            {
                foreach (var row in tran.SelectBackward<int,int>("t2"))
                {
                    Console.WriteLine("Key: {0}", row.Key);
                }
            }
        }

         private bool fcmp()
        {
            System.IO.FileStream fs1 = new FileStream(@"D:\temp\DBreezeTest\DBR1\1000000", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 8192);
            System.IO.FileStream fs2 = new FileStream(@"D:\temp\DBreezeTest\DBR2\1000000", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 8192);

            return StreamsContentsAreEqual(fs1, fs2);
        }

         private static bool StreamsContentsAreEqual(Stream stream1, Stream stream2)
         {
             const int bufferSize = 2048 * 2;
             var buffer1 = new byte[bufferSize];
             var buffer2 = new byte[bufferSize];

             int pos = 0;

             while (true)
             {
                 int count1 = stream1.Read(buffer1, 0, bufferSize);
                 int count2 = stream2.Read(buffer2, 0, bufferSize);

                 pos += bufferSize;

                 if (count1 != count2)
                 {
                     return false;
                 }

                 if (count1 == 0)
                 {
                     return true;
                 }

                 int iterations = (int)Math.Ceiling((double)count1 / sizeof(Int64));
                 for (int i = 0; i < iterations; i++)
                 {
                     if (BitConverter.ToInt64(buffer1, i * sizeof(Int64)) != BitConverter.ToInt64(buffer2, i * sizeof(Int64)))
                     {
                         return false;
                     }
                 }
             }
         }


         Dictionary<long, byte> _sharedDictionaryt1 = new Dictionary<long, byte>();
         Dictionary<long, byte> _tmpDictionaryt1 = new Dictionary<long, byte>();
         System.Threading.ReaderWriterLockSlim _sync_sharedDictionaryt1 = new ReaderWriterLockSlim();
         int isd = 0;
         int jjjir = 0;

         private void Fill_SDt1()
         {
             try 
	        {	        
		            using (var tran = engine.GetTransaction())
                        {
                            tran.SynchronizeTables("t1", "t2");

                            Random rnd = new Random();
                        int key=0;
                        DateTime dt = DateTime.UtcNow;
                        byte[] bt=null;

                        //_sync_sharedDictionaryt1.EnterWriteLock();
                        //try
                        //{
                        //    while (true)
                        //    {
                        //        key = rnd.Next(0, 1000000);
                        //        if (!_sharedDictionaryt1.ContainsKey(key))                            
                        //            break;
                        //    }

                        //    _sharedDictionaryt1.Add(key,1);
                    
                        //}
                        //finally
                        //{
                        //    _sync_sharedDictionaryt1.ExitWriteLock();
                        //}
                                               

                        isd = System.Threading.Interlocked.Add(ref isd,7);
                        isd++;

                        bt = dt.To_8_bytes_array().Concat(isd.To_4_bytes_array_BigEndian());

                        tran.Insert<byte[], byte>("t1", bt, 1);
                        tran.Insert<byte[], byte>("t2", bt, 1);

                        tran.Commit();                      
                        }

                    
	        }
	        catch (Exception ex)
	        {
		
	        }            

             Thread.Sleep(20);

             Fill_SDt1();
         }


         private void Read_SDt1()
         {
             try
             {
                 using (var tran = engine.GetTransaction())
                 {
                     Console.WriteLine("***");
                     foreach (var row in tran.SelectBackward<byte[], byte>("t1").Take(10))
                     {
                         Console.WriteLine("K: {0}", row.Key.ToBytesString(""));
                     }
                 }
             }
             catch (Exception ex)
             {
                 
             }

             Thread.Sleep(20);

             Read_SDt1();
         }

         private void testF_002()
         {
             Action a = () =>
             {
                 Fill_SDt1();
             };

             a.DoAsync();

             Action b = () =>
             {
                 Read_SDt1();
             };

             b.DoAsync();
         }

         
         private void ThreadAdd200000(string tName)
         {
             DBreeze.Diagnostic.SpeedStatistic.ToConsole = true;
             DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");

             using (var tran = engine.GetTransaction())
             {
                 DateTime dt = DateTime.MinValue;

                 int cnt = 0;
                 foreach (var row in tran.SelectForward<long, int>("t1"))
                 {
                     cnt++;
                 }

                 Console.WriteLine("C: {0}; V: {1}", tran.Count("t1"), cnt);


                 //tran.Technical_SetTable_OverwriteIsNotAllowed("t1");

                 for (int i = 0; i < 200; i++)
                 {

                                       //dt = dt.AddTicks(15);
                     for (int j = 1000; j < 2000; j++)
                     {
                         tran.Insert<long, int>("t" + i, j, 1);
                     }
                     //tran.Insert<long, int>("t1", dt.Ticks - 4, 2);
                 }

                 tran.Commit();
             }

             DBreeze.Diagnostic.SpeedStatistic.PrintOut("a",true);

          //   Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ms") +" " + tName + " started");


          //   if (true)
          //   {
          //       //Filling dictionary of memory
          //       var xfs = new FileStream(@"D:\temp\DBreezeTest\DBR1\test.txt", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 8192, FileOptions.None);
          //       xfs.Position = 0;
          //       byte[] bt=new byte[12];


          //       while (xfs.Read(bt, 0, 12) > 0)
          //       {
          //           DBreeze.Storage.MSR.qMp.Add(bt.Substring(0, 8).To_Int64_BigEndian(), bt.Substring(8, 4).To_Int32_BigEndian());
          //       }
          //       //xfs.Read(bt, 0, bt.Length);
          //       //foreach (var kvp in DBreeze.Storage.MSR.qMp)
          //       //{
          //       //    bt = kvp.Key.To_8_bytes_array_BigEndian().Concat(kvp.Value.To_4_bytes_array_BigEndian());
          //       //    xfs.Write(bt, 0, bt.Length);
          //       //}
                 
          //       xfs.Close();
          //       xfs.Dispose();
          //   }

          //   //DBreeze.Storage.MSR.qCalls = 0;
          //   //DBreeze.Storage.MSR.qLen = 0;
          //   //DBreeze.Storage.MSR.qMp.Clear();

          //   using (var tran = engine.GetTransaction())
          //   {
          //      // tran.Technical_SetTable_OverwriteIsNotAllowed("t1");

          //       //for (int i = 0; i < 10000; i++)
          //       for (int i = 10000; i < 20000; i++)
          //       {
          //           //if(zz==1)
          //           //    Console.WriteLine(i);
          //           //if (i == 10996)  //disk
          //           ////if (i == 10990)  //mem
          //           //{

          //           //}
          //           var row = tran.Select<int, int>("t1", i, false);
          //           //var row = tran.Select<int, int>("t1", i,false);
                     
          //           int val = 0;
          //           if (row.Exists)
          //           {
          //               val = row.Value;
          //           }

          //           tran.Insert<int, int>("t1", i, val + 1);
          //       }

          //       tran.Commit();
          //   }
          //   zz = 1;
          //   Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ms") + " " + tName + " gone");

          //   Console.WriteLine("qCnt: {0}; qLen: {1}; ", DBreeze.Storage.FSR.qCalls, DBreeze.Storage.FSR.qLen);
          ////   Console.WriteLine("qCnt: {0}; qLen: {1}; ", DBreeze.Storage.MSR.qCalls, DBreeze.Storage.MSR.qLen);


          //   if (false)
          //   {
                 
          //       byte[] bt=null;
          //       var xfs = new FileStream(@"D:\temp\DBreezeTest\DBR1\test.txt", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 8192, FileOptions.None);
          //       xfs.Position = xfs.Length;

          //       foreach (var kvp in DBreeze.Storage.MSR.qMp)
          //       {
          //           bt = kvp.Key.To_8_bytes_array_BigEndian().Concat(kvp.Value.To_4_bytes_array_BigEndian());
          //           xfs.Write(bt, 0, bt.Length);
          //       }

          //       xfs.Flush();
          //       xfs.Close();
          //       xfs.Dispose();
          //   }

             //List<KeyValuePair<long,int>> lst=new List<KeyValuePair<long,int>>();

             //in MSR and FSR in Table read counters

             //Save as byte array
             //foreach (var d in DBreeze.Storage.MSR.qMp)
             //{
             //    lst.Add(d);
             //}
             //string a = DBreeze.Utils.JavascriptSerializator.SerializeMJSON(DBreeze.Storage.MSR.qMp);
             //string a = DBreeze.Utils.XmlSerializator.SerializeXml(lst);
         }

        //2call
        //Disk
        //qCnt = 40139, len = 4524470
        //Mem
         //qCnt = 40132, len = 4518838


        private void testF_001()
        {

            Action<string> a = (trname) =>
            {
                ThreadAdd200000(trname);             
            };

            a.DoAsync("t1");
            ////a.DoAsync("t2");
            ////a.DoAsync("t3");

            //using (var tran = engine.GetTransaction())
            //{

            //    foreach (var row in tran.SelectForward<int, int>("t1", true))
            //    {
            //        Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
            //    }
            //}
            return;

            //using (var tran = engine.GetTransaction())
            //{

            //    tran.Insert<byte[], byte>("t1", new byte[] { 1 }, 1);
            //    //tran.Technical_SetTable_OverwriteIsNotAllowed("t1");

            //    //// for (int i = 0; i < 1000000; i++)
            //    //for (int i = 0; i < 3000; i++)
            //    //{
            //    //    tran.Insert<int, int>("t1", i, 3);
            //    //}

            //    tran.Commit();
            //}

            //using (var tran = engine.GetTransaction())
            //{
            //    tran.Technical_SetTable_OverwriteIsNotAllowed("t1");
            //    tran.Insert<byte[], byte>("t1", new byte[] { 2 }, 1);

            //    foreach (var row in tran.SelectForward<byte[], byte>("t1",true).Take(10))
            //    {
            //        Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
            //    }
            //    tran.Commit();
            //}


            //using (var tran = engine.GetTransaction())
            //{
            //    foreach (var row in tran.SelectForward<byte[], byte>("t1", true).Take(10))
            //    {
            //        Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
            //    }
            //}

            //return;


            ////Console.WriteLine(fcmp());
            ////return;

            //var LTrieSettings = new TrieSettings()
            //{

            //    //SkipStorageBuffer = true
            //};

            //IStorage Storage = new StorageLayer(Path.Combine(@"D:\temp\DBreezeTest\DBR1", "1000000"), LTrieSettings, new DBreezeConfiguration() { Storage = DBreezeConfiguration.eStorage.DISK, DBreezeDataFolderName = @"D:\temp\DBreezeTest\DBR1" });

            //LTrie = new LTrie(Storage);

            //LTrie.TableName = "1000000";

            ////var xrow = LTrie.GetKey(new byte[] { 1 },false);
            //for (int i = 0; i < 100000; i++)
            //{
            //    LTrie.Add(i.To_4_bytes_array_BigEndian(), new byte[] { 1 });
            //}

            //LTrie.Commit();

            //Console.WriteLine("done");
            //return;

            //var LTrieSettings = new TrieSettings()
            //{
                 
            //     //SkipStorageBuffer = true
            //};

            //IStorage Storage = new StorageLayer(Path.Combine(@"D:\temp\DBreezeTest\DBR1", "1000000"), LTrieSettings, new DBreezeConfiguration() { Storage = DBreezeConfiguration.eStorage.DISK, DBreezeDataFolderName = @"D:\temp\DBreezeTest\DBR1" });

            //LTrie = new LTrie(Storage);

            //LTrie.TableName = "1000000";

            ////var xrow = LTrie.GetKey(new byte[] { 1 },false);

            //LTrie.Add(new byte[] { 2 }, new byte[] { 1 });

            //LTrie.Commit();

            //return;


            //System.IO.DirectoryInfo di = new DirectoryInfo(@"D:\temp\DBreezeTest\DBR1\");
            //di.Delete(true);
            //di.Create();
            //FSR fsr = new FSR(@"D:\temp\DBreezeTest\DBR1\tmp", new TrieSettings(), new DBreezeConfiguration());

            //fsr.Table_WriteToTheEnd(new byte[] { 0, 1, 2, 3 });
            //fsr.Table_WriteByOffset(2, new byte[] { 4, 4 });
            ////fsr.Table_WriteToTheEnd(new byte[] { 4, 5, 6, 7, 8 });

            //byte[] btWork = fsr.Table_Read(false, 0, 20);

            //Console.WriteLine(btWork.ToBytesString(""));

            //return;

            DBreeze.Diagnostic.SpeedStatistic.ToConsole = true;
            DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            using (var tran = engine.GetTransaction())
            {
                //tran.Technical_SetTable_OverwriteIsNotAllowed("t1");
                //tran.Technical_SetTable_OverwriteIsNotAllowed("t2");

                //tran.Insert<int, int>("t1", 1, 4);
                //tran.Insert<int, int>("t2", 1, 4);
                //int i = 0;
                //i = i / i;

                //tran.Commit();

                Console.WriteLine("t1");
                foreach (var row in tran.SelectForward<int, int>("t1", true))
                {
                    Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                }
                Console.WriteLine("t2");
                foreach (var row in tran.SelectForward<int, int>("t2", true))
                {
                    Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                }




                //for (int i = 0; i < 10; i++)
                //{
                //    tran.Insert<int, int>("t1", i, 1);
                //}

                //Console.WriteLine("read" + tran.Count("t1"));
                //foreach (var row in tran.SelectForward<int, int>("t1", true))
                //{
                //    Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                //}

                //Console.WriteLine("write");
                //foreach (var row in tran.SelectForward<int, int>("t1", false))
                //{
                //    Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                //}

                //tran.Commit();


                //tran.Technical_SetTable_OverwriteIsNotAllowed("t1");

                //for (int i = 0; i < 10; i++)
                //{
                //    tran.Insert<int, int>("t1", i, 3);
                //}

                //Console.WriteLine("read");
                //foreach (var row in tran.SelectForward<int, int>("t1", true))
                //{
                //    Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                //}

                //Console.WriteLine("write");
                //foreach (var row in tran.SelectForward<int, int>("t1", false))
                //{
                //    Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                //}

                ////tran.Commit();
                //tran.Rollback();

                //Console.WriteLine("write2");
                //foreach (var row in tran.SelectForward<int, int>("t1", false))
                //{
                //    Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                //}







                //////////////////////////////////7

                    //Datablock test

                    // var row = tran.Select<int,byte[]>("t1",1);                
                    // byte[] ptrDatablock = null;
                    // if(row.Exists)
                    // {
                    //     ptrDatablock = row.Value;

                    //     byte[] test = tran.SelectDataBlock("t1", ptrDatablock);
                    //     Console.WriteLine("DB: {0}", test.ToBytesString(""));
                    //     return;
                    // }

                    //// ptrDatablock = tran.InsertDataBlock("t1", ptrDatablock, new byte[] { 1, 2, 2 });
                    // ptrDatablock = tran.InsertDataBlock("t1", ptrDatablock, null);


                    // tran.Insert<int, byte[]>("t1", 1, ptrDatablock);


                    // tran.Commit();

                    //int kk = 0;
                    //foreach (var row in tran.SelectBackward<int, int>("t1"))
                    //{
                    //    kk++;
                    //}
                    //Console.WriteLine("{0}, {1}", tran.Count("t1"), kk);


                    //tran.Technical_SetTable_OverwriteIsNotAllowed("t1");

                    //DateTime id = DateTime.MinValue;
                    //byte[] bt = null;
                    //bool wasUpdated = false;
                    //int jju = 0;

                    //for (int i = 0; i < 1000; i++)
                    //// for (int i = 0; i < 5000; i++)
                    //{
                    //    tran.Insert<int, int>("t1", i, 4);
                    //    //id = id.AddTicks(12);
                    //    //tran.Insert<long, int>("t1", id.Ticks, 4, out bt, out wasUpdated);
                    //    //if (wasUpdated)
                    //    //    jju++;
                    //}

                    //Console.WriteLine(jju);
                  //  tran.Commit();
            }

           

            //using (var tran = engine.GetTransaction())
            //{
            //    int two = 0;
            //    int three = 0;
            //    int val = 0;
            //    foreach (var row in tran.SelectForward<int, int>("t1"))
            //    {
            //        val = row.Value;
            //        //if (val == 2)
            //        //{
            //        //    two++;
            //        //}
            //        //else if (val == 3)
            //        //{
            //        //    three++;
            //        //}
            //        //else
            //        //{

            //        //}

            //        //Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
            //    }

            //    Console.WriteLine("2: {0}; 3: {1}", two, three);
            //}
            DBreeze.Diagnostic.SpeedStatistic.PrintOut("a",true);
            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("b", true);

            Console.WriteLine("done");
        }


        private void testC16()
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            using (var tran = engine.GetTransaction())
            {
               // tran.Technical_SetTable_OverwriteIsNotAllowed("t1");
                NestedTable nt = null;
                for (int i = 1; i <= 8000; i++)
                {
                    nt = tran.InsertTable<int>("t1", i, 0);
                    //nt.Technical_SetTable_OverwriteIsNotAllowed();
                    nt.Insert<int, int>(1, 1);
                }

                tran.Commit();
            }
            sw.Stop();
            Console.WriteLine("Consumed: {0}", sw.ElapsedMilliseconds);
        }

        private void testC14()
        {
            byte[] btGuid = null;

            Dictionary<string, byte[]> _d = new Dictionary<string, byte[]>();


            for (int i = 0; i < 100000; i++)
            {
                btGuid = Guid.NewGuid().ToByteArray();

                _d.Add(btGuid.ToBytesString(""), btGuid);
            }


            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            using (var tran = engine.GetTransaction())
            {
                NestedTable nt = null;
                nt = tran.InsertTable<int>("t1", 1, 0);

                foreach (var de in _d.OrderBy(r => r.Key))
                {
                    nt.Insert<byte[], byte>(de.Value, 1);

               //     tran.Insert<byte[], byte>("t1",de.Value, 1);

                }


                tran.Commit();




                Console.WriteLine("INSERTED: " + nt.Count());
                //Console.WriteLine("INSERTED: " + tran.Count("t1"));
            }

            sw.Stop();
            Console.WriteLine("Consumed: {0}", sw.ElapsedMilliseconds);

        }


        private void testC15()
        {
            Dictionary<string, byte[]> _d = new Dictionary<string, byte[]>();

            using (var tran = engine.GetTransaction())
            {
                NestedTable nt = null;
                nt = tran.SelectTable<int>("t1", 1, 0);

                foreach (var row in nt.SelectForward<byte[], byte>())
                {
                    _d.Add(row.Key.ToBytesString(""), row.Key);
                }

                //foreach (var row in tran.SelectForward<byte[], byte>("t1"))
                //{
                //    _d.Add(row.Key.ToBytesString(""), row.Key);
                //}
            }


            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            using (var tran = engine.GetTransaction())
            {
                   //tran.Technical_SetTable_OverwriteIsNotAllowed("t1");

                NestedTable nt = null;
                nt = tran.InsertTable<int>("t1", 1, 0);
                nt.Technical_SetTable_OverwriteIsNotAllowed();
              // tran.Technical_SetTable_OverwriteIsNotAllowed("t1");

                foreach (var de in _d)
                {
                    var row = nt.Select<byte[], byte>(de.Value, true);
                    if (row.Exists)
                    {
                        nt.RemoveKey<byte[]>(row.Key);
                    }

                    //var row = tran.Select<byte[], byte>("t1",de.Value, true);
                    //if (row.Exists)
                    //{
                    //    tran.RemoveKey<byte[]>("t1", row.Key);
                    //}
                }

                tran.Commit();

                //Console.WriteLine("INSERTED: " + tran.Count("t1"));
                Console.WriteLine("REMOVED: " + nt.Count());
            }

            sw.Stop();
            Console.WriteLine("Consumed: {0}", sw.ElapsedMilliseconds);
        }

        private void testC10()
        {

            
            new Thread(c110_tr1).Start();
            System.Threading.Thread.Sleep(200);
            new Thread(c110_tr4).Start();
            new Thread(c110_tr2).Start();
            new Thread(c110_tr3).Start();
            
        }

        private void c110_tr1()
        {
            try
            {
                int i = 0;
                int j = 0;

                using (var tran = engine.GetTransaction())
                {
                    //Console.WriteLine("tr1_p1");
                   // tran.SynchronizeTables("a2", "b7", "d*", "o");
                    Console.WriteLine("tr1");

                    //i = i / j;
                    System.Threading.Thread.Sleep(2000);
                    //Console.WriteLine("tr1_p2");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("kuku");
            }
            
        }

        private void c110_tr2()
        {
            using (var tran = engine.GetTransaction())
            {
                //Console.WriteLine("tr2_p1");
                tran.SynchronizeTables("a1", "a2", "d*","o");
                Console.WriteLine("tr2" + " " + DateTime.UtcNow.Ticks.ToString());
                System.Threading.Thread.Sleep(100);
                //Console.WriteLine("tr2_p2");
            }
        }

        private void c110_tr3()
        {
            using (var tran = engine.GetTransaction())
            {
                //Console.WriteLine("tr3_p1");
                tran.SynchronizeTables("a1", "a2", "d*");
                Console.WriteLine("tr3" + " " + DateTime.UtcNow.Ticks.ToString());
                System.Threading.Thread.Sleep(100);
                //Console.WriteLine("tr3_p2");
            }
        }

        private void c110_tr4()
        {
            using (var tran = engine.GetTransaction())
            {
                //Console.WriteLine("tr2_p1");
                tran.SynchronizeTables("b*","o");
                Console.WriteLine("tr4" + " " + DateTime.UtcNow.Ticks.ToString());
                System.Threading.Thread.Sleep(100);
                //Console.WriteLine("tr2_p2");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void testC9()
        {
            //Random rnd = new Random();
            //byte[] bt = new byte[20];
            //rnd.NextBytes(bt);

            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            //string a,b = "";
            //for (int i = 0; i < 1000000; i++)
            //{
            //    //a = bt.ToBytesString();
            //    b = Convert.ToBase64String(bt);
            //}
            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);
            //return;
            using (var tran = engine.GetTransaction())
            {
                NestedTable nt;

                nt = tran.InsertTable<byte>("t1", 1, 0);
                nt.Technical_SetTable_OverwriteIsNotAllowed();
                nt.InsertPart<ulong, uint>(1, 1, 32);
                nt.InsertPart<ulong, uint>(1, 1, 0);

                var row = nt.Select<ulong, byte[]>(1);
                Console.WriteLine(row.Value.ToBytesString(""));
            }
        }



        private void testC7()
        {
            byte[] key_13460 = { 8, 191, 215, 78, 51, 223, 181, 156 };



            using (var tran = engine.GetTransaction())
            {
                var table = "ntable";



                var tbl = tran.InsertTable(table, 100, 0u);

                for (int i = 0; i < 10500000; i++)
                {



                    var key = key_13460.Concat(BytesProcessing.To_16_bytes_array_BigEndian((decimal)i));

                    tran.Insert<byte[], byte[]>(table, key, null);

                    if (i % 100000 == 0)

                        Console.WriteLine(i);

                }



                tran.Commit();
            }

            Console.WriteLine("DONE");
        }


        private void testC8()
        {
            byte[] key_13460 = { 8, 191, 215, 78, 51, 223, 181, 156 };



            //var engine = new DBreezeEngine(@"d:\temp\dbreeze\");

            //var txn = engine.GetTransaction();

            var table = "ntable";

            var sw = System.Diagnostics.Stopwatch.StartNew();

            using (var tran = engine.GetTransaction())
            {

                for (int i = 100000; i < 100010; i++)
                {



                    var fkey = key_13460.Concat(BytesProcessing.To_16_bytes_array_BigEndian((decimal)i));

                    var tkey = key_13460.Concat(BytesProcessing.To_16_bytes_array_BigEndian((decimal)i + 1));

                    sw.Reset();
                    sw.Start();
                    //sw.Restart();

                    var c = tran.SelectForwardFromTo<byte[], byte[]>(table, fkey, true, tkey, true).GetEnumerator();

                    var l = 0;

                    if (c.MoveNext())
                    {

                        //Console.WriteLine("a-"+c.Current.Key.ToBytesString(""));
                        Console.WriteLine("a");

                        l = 1;

                        if (c.MoveNext())
                        {

                            //Console.WriteLine("b-" + c.Current.Key.ToBytesString(""));
                            Console.WriteLine("b");

                            l = 2;

                        }

                    }

                    sw.Stop();
                    var ms = sw.Elapsed.TotalMilliseconds;
                                        
                    Console.WriteLine("{0} {1}", ms, l);

                }

            }

            Console.WriteLine("DONE");
            //txn.Commit();
        }

        private void testC6()
        {
            //Console.WriteLine("INSERT");
            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            //using (var tran = engine.GetTransaction())
            //{
            //    //var tblPositions = tran.InsertTable<int>("t1", 1, 0);
            //    //tran.Technical_SetTable_OverwriteIsAllowed("t2", false);
            //    for (int i = 0; i < 100; i++)
            //    {
            //        tran.Insert<int, int>("t2",i, 1);
            //        //tblPositions.Insert<int, int>(i, i);
            //    }

            //    tran.Commit();
            //}
            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("a",true);


            using (var tran = engine.GetTransaction())
            {
                foreach (var row in tran.SelectForward<int, int>("t2").Take(10))
                {
                    Console.WriteLine(row.Value);
                }
            }
            return;

            Console.WriteLine("UPDATE");
            DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            using (var tran = engine.GetTransaction())
            {
                //var tblPositions = tran.InsertTable<int>("t1", 1, 0);
                //tblPositions.Technical_OverwritingOfTrieNodesIsAllowed(true);

                //tran.t.Technical_SetTable_OverwriteIsAllowed("t2", false);
               // tran.Technical_SetTable_OverwritingOfTrieNodesIsAllowed("t2", false);

                for (int i = 0; i < 100; i++)
                {                   
                    tran.Insert<int, int>("t2", i, 2);
                    //tblPositions.Insert<int, int>(i, i);
                }

                tran.Commit();
            }
            DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);

            using (var tran = engine.GetTransaction())
            {
                foreach (var row in tran.SelectForward<int, int>("t2").Take(10))
                {
                    Console.WriteLine(row.Value);
                }
            }
        }

        private void testC3()
        {
            using (var tran = engine.GetTransaction())
            {
                //DBreeze.Diagnostic.SpeedStatistic.StartCounter("t");
                //long cnt = 0;
                //foreach (var row in tran.SelectBackward<int, byte[]>("t1"))
                //{
                //    cnt++;
                //}

                //Console.WriteLine(cnt);
                //DBreeze.Diagnostic.SpeedStatistic.PrintOut("t", true);
                //return;

                DateTime bdt = new DateTime(1970, 1, 1);

                DBreeze.Diagnostic.SpeedStatistic.StartCounter("t");
                for (int i = 0; i < 1000000; i++)
                {
                    bdt = bdt.AddSeconds(7);
                    //tran.Insert<DateTime, byte[]>("t1", bdt, new byte[] { 1, 2, 3 });
                    tran.Insert<int, byte[]>("t1", i, new byte[] { 1, 2, 3 });
                }

                tran.Commit();
                DBreeze.Diagnostic.SpeedStatistic.PrintOut("t", true);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void testC5()
        {
            string tblP = "p" + "1";
            ulong mustCnt = 0;
            ulong cnt = 0;

            string pp = engine.Scheme.GetTablePathFromTableName(tblP);

            using (var tran = engine.GetTransaction())
            {
                mustCnt = tran.Count(tblP);

                foreach (var row in tran.SelectForward<byte[], byte[]>(tblP))
                {
                    cnt++;
                    //Console.WriteLine("Key: {0}", row.Key.ToBytesString(""));
                }

                if (mustCnt != cnt)
                {
                    Console.WriteLine("ERROR" + tran.Count(tblP));
                }
                Console.WriteLine(mustCnt);
            }

            Random rnd = new Random();
            byte[] btvalB = new byte[10 + rnd.Next(30)];
            DateTime udtBase = DateTime.UtcNow;

            using (var tran = engine.GetTransaction())
            {
                //string tblP = "p" + "1";
                string tblR = "r" + "1";

                tran.SynchronizeTables(tblP, tblR);

                //for (int i = 0; i < 30 + (rnd.Next(10)); i++) //this
                for (int i = 0; i < 30; i++)    //and this
                {

                    rnd.NextBytes(btvalB);
                    //udtBase = udtBase.AddTicks(rnd.Next(10000));
                    //udtBase = udtBase.AddSeconds(1 + rnd.Next(10));   //and This
                    //udtBase = udtBase.AddSeconds(i);  //and this
                    udtBase = udtBase.AddMilliseconds(i);
                    //udtBase = udtBase.AddSeconds(1 + rnd.Next(300));

                    tran.Insert<DateTime, byte[]>(tblP, udtBase, btvalB);
                    tran.Insert<byte[], DateTime>(tblR, new byte[] { 2 }, udtBase);
                }

                tran.Commit();
            }

            Console.WriteLine("DONE");
        }



        private void TestSFI1()
        {
           
            var table = "table";



            foreach (var j in Enumerable.Range(0, 2))
            {

                var txn = engine.GetTransaction();

                Console.WriteLine("Iter {0}", j);

                var n = 60000;
               

                var nExists = 0;

                Dictionary<ulong, int> sd = new Dictionary<ulong, int>();

                foreach (var i in Enumerable.Range(0, n))
                {

                    var str = "Item_" + i.ToString();

                    var hash = DBreeze.Utils.Hash.MurMurHash.MixedMurMurHash3_64(System.Text.UTF8Encoding.Default.GetBytes(str));
                                        
                    //var hash = (ulong)i;
                    //if(sd.ContainsKey(hash))
                    //{
                    //    Console.WriteLine(">>>>>>>>" + i.ToString());
                    //}
                    sd.Add(hash, 0);

                    //var fkey = BytesProcessing.To_8_bytes_array_BigEndian(hash);
                    //Console.WriteLine(fkey.ToBytesString(""));

                    //txn.Insert<byte[], int>(table, fkey, 0);




                    //var row = txn.Select<byte[], int>(table, fkey);

                    //if (row.Exists)
                    //{

                    //    txn.Insert<byte[], int>(table, fkey, 1);

                    //    nExists = nExists + 1;

                    //}
                    //else
                    //{
                    //    txn.Insert<byte[], int>(table, fkey, 0);
                    //}

                }




                //foreach (var el in sd.OrderBy(r=>r.Key))
                foreach (var el in sd)
                {
                    var fkey = BytesProcessing.To_8_bytes_array_BigEndian(el.Key);
                    txn.Insert<byte[], int>(table, fkey, el.Value);
                   
                }

                Console.WriteLine("Commit total: {0} exists {1}", txn.Count(table), nExists);

                txn.Commit();

                var ctr = 0;

                foreach (var row in txn.SelectForward<byte[], byte[]>(table))
                    ctr = ctr + 1;

                Console.WriteLine("Forward scan yields {0} rows", ctr);

                txn.Dispose();

            }
        }


        private void TestSFI2()
        {
              var table = "table";

              engine.Scheme.DeleteTable(table);

            byte[] key_13460 = { 8, 191, 215, 78, 51, 223, 181, 156 };  // 8-byte key obtained from 64-bit murmurhash3 of string “ssn:Sensor_13460”

            var txn = engine.GetTransaction();

          



            foreach (var i in Enumerable.Range(0, 20000))
            {

                var str = "ssn:Sensor_" + i.ToString();

                var bytes = System.Text.UTF8Encoding.Default.GetBytes(str);

                var hash = DBreeze.Utils.Hash.MurMurHash.MixedMurMurHash3_64(bytes);

                var key_i = BytesProcessing.To_8_bytes_array_BigEndian(hash);



                var row_13460 = txn.Select<byte[], int>(table, key_13460);

                var e1 = row_13460.Exists;



                //var row2 = txn.Select<byte[], int>(table, key_i);

                //var e2 = row2.Exists;

                txn.Insert(table, key_i, 1);



                //var row3 = txn.Select<byte[], int>(table, key_i);

                //var e3 = row3.Exists;



               // if (i > 13440 && i < 13470)
                if (i > 13440 && i < 13500)
                {

                   // Console.WriteLine("{0} {1} {2} {3}", str, e1, e2, e3);
                    Console.WriteLine("{0} {1}", str, e1);

                }



            }

            txn.Dispose();

        }


        private void TestMixedStorageMode()
        {
            //Console.WriteLine(engine.Scheme.IfUserTableExists("t1"));
            //Console.WriteLine(engine.Scheme.IfUserTableExists("t11"));
            //engine.Scheme.DeleteTable("t11");
            //Console.WriteLine(engine.Scheme.IfUserTableExists("t11"));

            using (var tran = engine.GetTransaction())
            {
                
                //tran.Insert<int, int>("t1", 1, 1);
                //tran.Insert<int, int>("t1", 15, 15);
                tran.Insert<int, int>("mem_t11", 2, 2);
                //tran.Insert<int, int>("mem_t12", 4, 4);
                //tran.Insert<int, int>("t2", 3, 3);

                tran.Commit();
            }

            engine.Scheme.RenameTable("mem_t11", "mem_t12");

            //Console.WriteLine(engine.Scheme.IfUserTableExists("t1"));

            using (var tran = engine.GetTransaction())
            {


                //foreach (var row in tran.SelectBackward<int, int>("t1"))
                //{
                //    Console.WriteLine("R: {0}; V: {1}; ", row.Key, row.Value);
                //}
                foreach (var row in tran.SelectBackward<int, int>("mem_t12"))
                {
                    Console.WriteLine("R: {0}; V: {1}; ", row.Key, row.Value);
                }
                //foreach (var row in tran.SelectBackward<int, int>("mem_t12"))
                //{
                //    Console.WriteLine("R: {0}; V: {1}; ", row.Key, row.Value);
                //}
                //foreach (var row in tran.SelectBackward<int, int>("t2"))
                //{
                //    Console.WriteLine("R: {0}; V: {1}; ", row.Key, row.Value);
                //}
            }
        }

        private void TestClosestToPrefix()
        {
           
            engine.Scheme.DeleteTable("t1");

            using (var tran = engine.GetTransaction())
            {
                //tran.Insert<string, byte>("t1", "agile", 1);
                //tran.Insert<string, byte>("t1", "sak", 1);
                //tran.Insert<string, byte>("t1", "sleeping", 1);
                //tran.Insert<string, byte>("t1", "sleeping", 1);
                //tran.Insert<string, byte>("t1", "slaki", 1);
                //tran.Insert<string, byte>("t1", "slart", 1);
                //tran.Insert<string, byte>("t1", "slort", 1);
                //tran.Insert<string, byte>("t1", "swamp", 1);
                //tran.Insert<string, byte>("t1", "zindex", 1);

                tran.Insert<string, byte>("t1", "check", 1);
                tran.Insert<string, byte>("t1", "sam", 1);
                tran.Insert<string, byte>("t1", "slaaash", 1);
                tran.Insert<string, byte>("t1", "slash", 1);
                tran.Insert<string, byte>("t1", "slam", 1);
                tran.Insert<string, byte>("t1", "slim", 1);
                tran.Insert<string, byte>("t1", "w", 1);
                tran.Insert<string, byte>("t1", "ww", 1);
                tran.Insert<string, byte>("t1", "www", 1);
                tran.Insert<string, byte>("t1", "wwww", 1);
                //tran.Insert<string, byte>("t1", "wwa", 1);
                //tran.Insert<string, byte>("t1", "wwah", 1);
                //tran.Insert<string, byte>("t1", "wwww", 1);
                //tran.Insert<string, byte>("t1", "wwwwa", 1);
                //tran.Insert<string, byte>("t1", "wwwwa", 1);
                tran.Insert<string, byte>("t1", "p", 1);

                //"check"
                //"sam"
                //"slash"
                //"slam"
                //"what"

                tran.Commit();
            }

            string prefix = "slap";
            //prefix = "www";
            //prefix = "wwwww";

            using (var tran = engine.GetTransaction())
            {
                //foreach (var row in tran.SelectForwardStartsWithClosestToPrefix<string, byte>("t1", prefix))
                foreach (var row in tran.SelectBackwardStartsWithClosestToPrefix<string, byte>("t1", prefix))
                //foreach (var row in tran.SelectForwardStartsWith<string, byte>("t1", prefix))
                {
                    Console.WriteLine(row.Key);
                }
            }
        }



        DBreeze.Storage.MemoryStorage ms = null;

        DBreeze.DBreezeEngine memoryEngine = null;

        private void TestMemoryStorage()
        {
            if (memoryEngine == null)
            {
                memoryEngine = new DBreezeEngine(new DBreezeConfiguration()
                {
                     Storage = DBreezeConfiguration.eStorage.MEMORY
                });
            }

            //SortedDictionary<string, int> _d = new SortedDictionary<string, int>();

            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            //DateTime dt=new DateTime(1970,1,1);

            //for (int i = 0; i < 1000000; i++)
            //{
            //    _d.Add(dt.Ticks.ToString(), i);
            //    dt=dt.AddSeconds(7);
            //}

            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);

            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            //int c1 = 0;
            //foreach (var row in _d.OrderBy(r => r.Key))
            //{
            //    c1++;
            //}
            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);
            //Console.WriteLine(c1);

            //memoryEngine.Scheme.DeleteTable("t1");

            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            ////DateTime dt = new DateTime(1970, 1, 1);

            //using (var tran = memoryEngine.GetTransaction())
            //{
            //    for (int i = 0; i < 1000000; i++)
            //    {
            //  //      tran.Insert<string, int>("t1", dt.Ticks.ToString(), i);
            //    //    dt = dt.AddSeconds(7);

            //        tran.Insert<byte[], byte[]>("t1", i.To_4_bytes_array_BigEndian(), i.To_4_bytes_array_BigEndian());

            //    }

            //    //Console.WriteLine(tran.Count("t1"));
            //    tran.Commit();
            //}

            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);


            //int c1 = 0;

            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");

            //using (var tran = memoryEngine.GetTransaction())
            //{
            //    DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");

            //    foreach (var row in tran.SelectForward<string, int>("t1"))
            //    {
            //        c1++;
            //    }

            //    DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);
            //    Console.WriteLine(c1);
            //}



            ms = new MemoryStorage(10, 10, MemoryStorage.eMemoryExpandStartegy.FIXED_LENGTH_INCREASE);

            ms.Write_ToTheEnd(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 });
            ms.Write_ToTheEnd(new byte[] { 1, 1, 1, 1, 1 });
            //ms.Write_ByOffset(17,new byte[] { 1, 1, 1, 1, 1 });
            //Console.WriteLine(ms.MemorySize);
            //Console.WriteLine(ms.Read(0, ms.MemorySize).ToBytesString(""));
            Console.WriteLine(ms.GetFullData().Length);
        }

        private void TestNewStorageLayer()
        {

            using (var tran = engine.GetTransaction())
            {
                byte[] ptr = null;
                ptr = tran.InsertDataBlock("t1", ptr, new byte[] { 1, 2, 3 });

                tran.Insert<int, byte[]>("t1", 1, ptr);

                var row = tran.Select<int, byte[]>("t1", 1);

                byte[] db = tran.SelectDataBlock("t1", row.Value);
                Console.WriteLine(db.ToBytesString(""));
                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {

                byte[] db = tran.SelectDataBlock("t1", tran.Select<int, byte[]>("t1", 1).Value);
                Console.WriteLine(db.ToBytesString(""));
            }
            return;

            DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            using (var tran = engine.GetTransaction())
            {
                for (int i = 0; i < 1000000; i++)
                {
                    tran.Insert<int, int>("t1", i, i);
                }

                Console.WriteLine(tran.Count("t1"));
                tran.Commit();
            }

            DBreeze.Diagnostic.SpeedStatistic.PrintOut("a",true);

            DBreeze.Diagnostic.SpeedStatistic.PrintOut("Table_WriteToTheEnd", true);
            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");

            //int cnt = 0;
            //using (var tran = engine.GetTransaction())
            //{
            //    foreach (var row in tran.SelectForward<int, int>("t1"))
            //    {
            //        cnt++;
            //    }
            //}

            //Console.WriteLine(cnt);
            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);

            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            //using (var tran = engine.GetTransaction())
            //{
            //    for (int i = 0; i < 1000000; i++)
            //    {
            //        tran.Insert<int, int>("t1", i, i);
            //    }

            //    Console.WriteLine(tran.Count("t1"));
            //    tran.Commit();
            //}

            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);

            //using (var tran = engine.GetTransaction())
            //{
            //    Console.WriteLine(tran.Count("t1"));
            //}
        }



        private void TestSecondaryIndexPreparation()
        {
            engine.Scheme.DeleteTable("t1");

            using (var tran = engine.GetTransaction())
            {
                tran.Insert<int, int>("t1", 5, 1);

                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {
                byte[] pointer=null;
                bool WasUpdated=false;

                tran.Insert<int, int>("t1", 5, 2, out pointer, out WasUpdated);

                if (WasUpdated)
                {
                    tran.Select<int, int>("t1", 5, true).PrintOut();
                }
              
                //tran.Insert<int, int>("t1", 5, 1, out pointer, out WasUpdated);

                ////tran.InsertPart<int, byte[]>("t1", 6, ((int?)null).To_5_bytes_array_BigEndian(), 18, out pointer, out WasUpdated);
                ////tran.InsertPart<int, byte[]>("t1", 6, new byte[] {1,2,3}, 18, out pointer, out WasUpdated);

                //tran.InsertPart<int, int?>("t1", 5, Int32.MaxValue, 18, out pointer, out WasUpdated);

                //string a = tran.Select<int, byte[]>("t1", 6).Value.ToBytesString("");

                //tran.ChangeKey<int>("t1", 6, 7, out pointer, out WasUpdated);

                //tran.RemoveKey<int>("t1", 7, out WasUpdated);

                //Console.WriteLine(WasUpdated);

                tran.Commit();
            }
        }

        //private void TestLinkToValue()
        //{

        //}

        private void TestIteratorsv11()
        {
            engine.Scheme.DeleteTable("t1");
            DBreeze.Diagnostic.SpeedStatistic.StartCounter("c1");

            using (var tran = engine.GetTransaction())
            {
                for (int i = 0; i < 1000000; i++)
                {
                    tran.Insert<int, int>("t1", i, i);
                }
            }

            DBreeze.Diagnostic.SpeedStatistic.PrintOut("c1",true);
        }



        private void TestIterators()
        {
            engine.Scheme.DeleteTable("t1");


            //using (var tran = engine.GetTransaction())
            //{

            //    for (int i = -200000; i < 800000; i+=2)
            //    {
            //        tran.Insert<int, int>("t1", i, i);
            //    }

            //    ulong kk = tran.Count("t1");

            //    int cnt = 0;
            //    int pq = -200000 + 1;
            //    foreach (var row in tran.SelectForward<int, int>("t1"))
            //    {
            //        cnt++;
            //        tran.Insert<int, int>("t1", pq, pq);
            //        pq+=2;
            //    }

            //    Console.WriteLine("Count: {0}", cnt);

            //    kk = tran.Count("t1");

            //    //cnt = 0;
            //    //foreach (var row in tran.SelectForward<int, int>("t1"))
            //    //{
            //    //    cnt++;
            //    //}

            //    //Console.WriteLine("Count: {0}", cnt);


            //    tran.Commit();

            //    //cnt = 0;
            //    //foreach (var row in tran.SelectForward<int, int>("t1"))
            //    //{
            //    //    cnt++;
            //    //}

            //    //Console.WriteLine("Count: {0}", cnt);
            //}

            //return;





            //using (var tran = engine.GetTransaction())
            //{

            //    for (int i = -200000; i < 800000; i++)
            //    {
            //        var tbl = tran.InsertTable<int>("t1", 1, 0);
            //        tbl.Insert<int, int>(i, i);
            //    }

            //    tran.Commit();
            //}

            //using (var tran = engine.GetTransaction())
            //{

            //    tran.SynchronizeTables("t1");

            //    var tbl = tran.InsertTable<int>("t1", 1, 0);

            //    int cnt = 0;
            //    int pq = 799999;
            //    foreach (var row in tbl.SelectForward<int, int>(true))
            //    {
            //        cnt++;
            //        tbl.RemoveKey<int>(pq);
            //        pq--;
            //    }

            //    Console.WriteLine("Count: {0}", cnt);

            //    Console.WriteLine("Max: {0}", tbl.Max<int, int>().Key);


            //    cnt = 0;
            //    foreach (var row in tbl.SelectForward<int, int>())
            //    {
            //        cnt++;
            //    }

            //    Console.WriteLine("Count: {0}", cnt);


            //    tran.Commit();

            //    cnt = 0;
            //    foreach (var row in tbl.SelectForward<int, int>())
            //    {
            //        cnt++;
            //    }

            //    Console.WriteLine("Count: {0}", cnt);
            //}

            //return;












            using (var tran = engine.GetTransaction())
            {

                for (int i = -200000; i < 800000; i++)
                {
                    tran.Insert<int, int>("t1", i, i);
                }

                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {

                //for (int i = -200000; i < 800000; i++)
                //{
                //    tran.Insert<int, int>("t1", i, i);
                //}
                tran.SynchronizeTables("t1");

                //ulong kk = tran.Count("t1");
               
                int cnt = 0;
                int pq = 799999;
                foreach (var row in tran.SelectForward<int, int>("t1", true))
                {
                    cnt++;
                    tran.RemoveKey<int>("t1", pq);
                    pq--;
                }

                Console.WriteLine("Count: {0}", cnt);

                Console.WriteLine("Max: {0}", tran.Max<int, int>("t1").Key);


                cnt = 0;
                foreach (var row in tran.SelectForward<int, int>("t1"))
                {
                    cnt++;
                }

                Console.WriteLine("Count: {0}", cnt);


                tran.Commit();

                cnt = 0;
                foreach (var row in tran.SelectForward<int, int>("t1"))
                {
                    cnt++;
                }

                Console.WriteLine("Count: {0}", cnt);
            }

            return;




            //using (var tran = engine.GetTransaction())
            //{

            //    for (int i = -200000; i < 800000; i++)
            //    {
            //        tran.Insert<int, int>("t1", i, i);
            //    }

            //    ulong kk = tran.Count("t1");

            //    int cnt = 0;
            //    int pq = 900000;
            //    foreach (var row in tran.SelectForward<int, int>("t1"))
            //    {
            //        cnt++;
            //        tran.Insert<int,int>("t1", pq,pq);
            //        pq++;
            //    }

            //    Console.WriteLine("Count: {0}", cnt);


            //    cnt = 0;
            //    foreach (var row in tran.SelectForward<int, int>("t1"))
            //    {
            //        cnt++;
            //    }

            //    Console.WriteLine("Count: {0}", cnt);


            //    tran.Commit();

            //    cnt = 0;
            //    foreach (var row in tran.SelectForward<int, int>("t1"))
            //    {
            //        cnt++;
            //    }

            //    Console.WriteLine("Count: {0}", cnt);
            //}

            //return;




            using (var tran = engine.GetTransaction())
            {               

                for (int i = -200000; i < 800000; i++)
                {
                    tran.Insert<int, int>("t1", i, i);
                }

                ulong kk = tran.Count("t1");

                int cnt = 0;
                int pq = 799999;
                foreach (var row in tran.SelectForward<int, int>("t1"))
                {
                    cnt++;
                    tran.RemoveKey<int>("t1",pq);
                    pq--;
                }               

                Console.WriteLine("Count: {0}", cnt);

              
                cnt = 0;
                foreach (var row in tran.SelectForward<int, int>("t1"))
                {
                    cnt++;
                }

                Console.WriteLine("Count: {0}", cnt);


                tran.Commit();

                cnt = 0;
                foreach (var row in tran.SelectForward<int, int>("t1"))
                {
                    cnt++;
                }

                Console.WriteLine("Count: {0}", cnt);
            }

            return;



            using (var tran = engine.GetTransaction())
            {
                var tbl = tran.InsertTable<int>("t1", 1, 0);

                for (int i = -200000; i < 800000; i++)
                {
                    tbl.Insert<int, int>(i, i);
                }             
                                
                int cnt = 0;
                foreach (var row in tbl.SelectBackwardFromTo<int, int>(600000,true,-100000,true))
                {
                    cnt++;
                    tbl.RemoveKey<int>(row.Key);
                }
                //foreach (var row in tbl.SelectBackward<int, int>())
                //{
                //    cnt++;
                //    tbl.RemoveKey<int>(row.Key);
                //}  

                Console.WriteLine("Count: {0}", cnt);

                tbl = tran.SelectTable<int>("t1", 1, 0);

                cnt = 0;
                foreach (var row in tbl.SelectBackward<int, int>())
                {
                    cnt++;                 
                }                

                Console.WriteLine("Count: {0}", cnt);
                

                tran.Commit();

                tbl = tran.SelectTable<int>("t1", 1, 0);

                cnt = 0;
                foreach (var row in tbl.SelectBackward<int, int>())
                {
                    cnt++;
                }

                Console.WriteLine("Count: {0}", cnt);
            }

            return;

            //using (var tran = engine.GetTransaction())
            //{
            //    tran.Insert<int, int>("t1", 10000, 1);
            //    tran.Insert<int, int>("t1", 20000, 1);
            //    tran.Insert<int, int>("t1", 30000, 1);

            //    tran.Commit();
            //}

            //using (var tran = engine.GetTransaction())
            //{
            //    var en = tran.SelectForward<int, int>("t1").GetEnumerator();

            //    bool first = true;

            //    while (en.MoveNext())
            //    {
            //        Console.WriteLine("K: {0}; V: {1}", en.Current.Key, en.Current.Value);

            //        if (first)
            //        {
            //            first = false;

            //            tran.Insert<int, int>("t1", 150000000, 2);

            //            //tran.RemoveKey<byte[]>("t1", new byte[] { 3, 1 });
            //            //tran.RemoveKey<byte[]>("t1", en.Current.Key);
            //        }
            //    }

            //    en.Dispose();

            //    tran.Commit();

            //    Console.WriteLine("****************");

            //    foreach (var row in tran.SelectForward<int, int>("t1"))
            //    {
            //        Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
            //    }


            //}

            //return;




            //using (var tran = engine.GetTransaction())
            //{
            //    tran.Insert<byte[], int>("t1", new byte[] { 1, 1 }, 1);
            //    tran.Insert<byte[], int>("t1", new byte[] { 2, 1 }, 1);
            //    tran.Insert<byte[], int>("t1", new byte[] { 3, 1 }, 1);
            //    tran.Insert<byte[], int>("t1", new byte[] { 3, 4, 8 }, 1);
            //    tran.Insert<byte[], int>("t1", new byte[] { 3, 4, 6 }, 1);
                

            //    tran.Commit();
            //}

            using (var tran = engine.GetTransaction())
            {
                bool first = true;

                //not, actually, necessary in this example
                //tran.SynchronizeTables("t1");

                tran.Insert<byte[], int>("t1", new byte[] { 1, 1 }, 1);
                tran.Insert<byte[], int>("t1", new byte[] { 2, 1 }, 1);
                tran.Insert<byte[], int>("t1", new byte[] { 3, 1 }, 1);
                tran.Insert<byte[], int>("t1", new byte[] { 3, 4, 8 }, 1);
                tran.Insert<byte[], int>("t1", new byte[] { 3, 4, 6 }, 1);





                //foreach (var row in tran.SelectForward<byte[], int>("t1"))
                //{
                //    Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);

                //    if (first)
                //    {
                //        first = false;

                //        tran.Insert<byte[], int>("t1", new byte[] { 3, 4, 9 }, 2);
                //        tran.RemoveKey<byte[]>("t1", new byte[] { 3, 4, 8 });
                //        tran.RemoveKey<byte[]>("t1", new byte[] { 3, 1 });
                //    }
                //}

                //OR REMARK FOREACH AND UN-REMARK NEXT CODE BLOCK TILL tran.Commit() (CODE REPEATS THE SAME BEHAVIOUR)
                var en = tran.SelectForward<byte[], int>("t1").GetEnumerator();

                while (en.MoveNext())
                {
                    Console.WriteLine("K: {0}; V: {1}", en.Current.Key.ToBytesString(""), en.Current.Value);

                    if (first)
                    {
                        first = false;

                        tran.Insert<byte[], int>("t1", new byte[] { 3, 4, 9 }, 2);
                        tran.RemoveKey<byte[]>("t1", new byte[] { 3, 4, 8 });
                        tran.RemoveKey<byte[]>("t1", new byte[] { 3, 1 });
                    }
                }

                en.Dispose();

                Console.WriteLine("*******OUT 1*********");

                foreach (var row in tran.SelectForward<byte[], int>("t1"))
                {
                    Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
                }

                tran.Commit();

                Console.WriteLine("********OUT 2********");

                foreach (var row in tran.SelectForward<byte[], int>("t1"))
                {
                    Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
                }


            }

            return;



            using (var tran = engine.GetTransaction())
            {
                for (int i = 0; i < 20; i++)
                {
                    tran.Insert<byte[], int>("t1", new byte[] { (byte)i }, i);
                }

                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {
                var en = tran.SelectForward<byte[], int>("t1").GetEnumerator();

                bool first = true;

                while (en.MoveNext())
                {
                    Console.WriteLine("K: {0}; V: {1}", en.Current.Key.ToBytesString(""), en.Current.Value);

                    if (first)
                    {
                        first = false;
                        tran.RemoveKey<byte[]>("t1", new byte[] {5});
                        //tran.RemoveKey<byte[]>("t1", en.Current.Key);
                    }
                }

                //foreach (var row in tran.SelectForward<byte[], int>("t1"))
                //{                  
                //    Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
                //    tran.RemoveKey<byte[]>("t1", row.Key);
                //}

                tran.Commit();
            }

            //using (var tran = engine.GetTransaction())
            //{
            //    Console.WriteLine("start");

            //    foreach (var row in tran.SelectForward<byte[], int>("t1"))
            //    {
            //        Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
                  
            //    }

            //    Console.WriteLine("DONE");
            //}
        }


        private void testbackwardwithStrings()
        {
            using (var tran = engine.GetTransaction())
            {
                tran.Insert<string, byte?>("t1", "w", null);
                tran.Insert<string, byte?>("t1", "a", null);
                tran.Insert<string, byte?>("t1", "aw", null);
                tran.Insert<string, byte?>("t1", "wwww", null);
                tran.Insert<string, byte?>("t1", "www", null);
                tran.Insert<string, byte?>("t1", "zzzw", null);
                tran.Insert<string, byte?>("t1", "bbww", null);
                tran.Insert<string, byte?>("t1", "z", null);
                tran.Insert<string, byte?>("t1", "wwwwwwwaww", null);
                tran.Insert<string, byte?>("t1", "wwwwwwwabw", null);
                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {
                //foreach (var row in tran.SelectBackwardStartsWith<string, byte?>
                //        ("t1", "wwww"))
                    foreach (var row in tran.SelectForwardStartsWith<string, byte?>
                            ("t1", "www"))

                //foreach (var row in tran.SelectBackwardStartFrom<byte[], byte?>("t1", new byte[] { 3, 3, 3, 3 ,3 },true))
                //foreach (var row in tran.SelectBackward<byte[], byte?>("t1"))
                //foreach (var row in tran.SelectBackwardFromTo<byte[], byte?>("t1", new byte[] { 3, 3, 3, 3 }, true, new byte[] { 3, 0, 0, 0 }, true))
                {
                    Console.WriteLine("{0}", row.Key);
                   
                }
            }

            Console.WriteLine("*******************");
        }

        #region "BUILDING MATRIX"

        int BM_count = 0;       
        int BM_rowlen = 4;
        int BM_deep = 4;
        bool BM_console_out_build = false;

        private void MATRIX_BUILD_RECURSE(int q, int xgl, byte[] parent,DBreeze.Transactions.Transaction tran)
        {
            if (xgl > BM_deep)
                return;

            xgl++;

            byte[] actual = parent;

           

            for (int i = 0; i < BM_rowlen; i++)
            {
                BM_count++;
                actual = parent.Concat(new byte[] { (byte)i });

                if(BM_console_out_build)
                    Console.WriteLine(actual.ToBytesString(""));

                tran.Insert<byte[], byte?>("t1", actual, null);

                MATRIX_BUILD_RECURSE(i, xgl, actual,tran);
            }
        }

        private void MATRIX_BUILD()
        {
            byte[] actual = null;

            using (var tran = engine.GetTransaction())
            {
                for (int i = 0; i < BM_rowlen; i++)
                {
                    BM_count++;
                    actual = new byte[] { (byte)i };

                    if (BM_console_out_build)
                        Console.WriteLine(actual.ToBytesString(""));

                    tran.Insert<byte[], byte?>("t1", actual, null);

                    MATRIX_BUILD_RECURSE(i, 1, actual,tran);
                }

                tran.Commit();
            }

            Console.WriteLine(BM_count);
        }

        private void MATRIX_READOUT_V1()
        {
            using (var tran = engine.GetTransaction())
            {
                ulong tc = tran.Count("t1");
                Console.WriteLine("Table Count: {0}", tc);
                ulong ic = 0;
                foreach (var row in tran.SelectForward<byte[], byte?>("t1"))
                {
                    Console.WriteLine("{0}", row.Key.ToBytesString(""));
                    ic++;
                }
                Console.WriteLine("Iteration Count: {0}; Equals: {1}", tc, (tc == ic).ToString());
            }
        }


        private void MATRIX_READOUT_V2()
        {
            using (var tran = engine.GetTransaction())
            {
                ulong tc = tran.Count("t1");
                Console.WriteLine("Table Count: {0}", tc);
                ulong bic = 0;
                ulong fic = 0;


                //foreach (var row in tran.SelectBackwardStartsWith<byte[], byte?>
                //    ("t1", new byte[] { 3, 3, 3, 3 }))
                //("t1", new byte[] { 3 }))
                //foreach (var row in tran.SelectBackwardStartsWith<byte[], byte?>("t1", new byte[] { 3, 3,3,3,3 }))

                //foreach (var row in tran.SelectBackwardFromTo<byte[], byte?>("t1", new byte[] { 3, 3, 3, 3,3 }, true, new byte[] { 0 }, true))

                foreach (var row in tran.SelectBackwardStartFrom<byte[], byte?>
                    ("t1", new byte[] { 3, 3, 3 },true))

                //foreach (var row in tran.SelectBackwardStartFrom<byte[], byte?>("t1", new byte[] { 3, 3, 3, 3 ,3 },true))
                //foreach (var row in tran.SelectBackward<byte[], byte?>("t1"))
                //foreach (var row in tran.SelectBackwardFromTo<byte[], byte?>("t1", new byte[] { 3, 3, 3, 3 }, true, new byte[] { 3, 0, 0, 0 }, true))
                {
                    Console.WriteLine("{0}", row.Key.ToBytesString(""));
                    bic++;
                }

                Console.WriteLine("*******************");

                //foreach (var row in tran.SelectForwardStartsWith<byte[], byte?>("t1",
                //    //    //new byte[] { 3, 3, 3, 3 }))
                //    new byte[] { 3, 3, 3,3,3 }))
                ////foreach (var row in tran.SelectForwardStartsWith<byte[], byte?>("t1", new byte[] { 3, 3,3,3,3}))

                ////foreach (var row in tran.SelectForwardFromTo<byte[], byte?>("t1", new byte[] { 0 }, true, new byte[] { 3, 3, 3, 3, 3 }, true))

                ////foreach (var row in tran.SelectForwardStartFrom<byte[], byte?>("t1", new byte[] { 0 }, true))
                ////foreach (var row in tran.SelectForward<byte[], byte?>("t1"))                
                ////foreach (var row in tran.SelectForwardFromTo<byte[], byte?>("t1", new byte[] { 3, 0, 0, 0 }, true, new byte[] { 3, 3, 3, 3 }, true))
                //{
                //    Console.WriteLine("{0}", row.Key.ToBytesString(""));
                //    fic++;
                //}

                Console.WriteLine("BackCnt: {0}; FwdCnt: {1}; Equals: {2}", bic, fic,(bic==fic).ToString());
            }
        }

        #endregion


        private void TestSelectBackwardStartWith_WRITE()
        {
            List<byte[]> dat = new List<byte[]>();
            Random rnd = new Random();
            byte[] buf = new byte[12];

            using (var tran = engine.GetTransaction())
            {
                for (int i = 0; i < 100; i++)
                {

                    rnd.NextBytes(buf);
                    //buf = (new byte[] { 12 }).Concat(buf);
                    //buf = (new byte[] { 58,58,58,58,58,58,58,58 }).Concat(buf);
                    buf = (new byte[] { 57, 57, 57, 57, 57, 57, 57 }).Concat(buf);
                    tran.Insert<byte[], byte?>("t1", buf, null);
                    buf = new byte[12];
                }

                tran.Commit();
            }

            Console.WriteLine("done");
            
        }


        private void TestSelectBackwardStartWith_READ()
        {
            using (var tran = engine.GetTransaction())
            {
                //foreach (var row in tran.SelectForwardStartsWith<byte[], byte?>("t1", new byte[] { }))            //!!!!!!!!!   check 0 byte later for StartWith
                //foreach (var row in tran.SelectForward<byte[], byte?>("t1"))


                //foreach (var row in tran.SelectForwardStartsWith<byte[], byte?>("t1", new byte[] { 12 }))                
                foreach (var row in tran.SelectBackwardStartsWith<byte[], byte?>("t1", new byte[] { 57,57,57,57,57,57 }))                
                {
                    Console.WriteLine(row.Key.ToBytesString(""));
                }
            }
        }

        private void TestBackUp()
        {
            //using (var tran = engine.GetTransaction())
            //{
            //    //tran.Insert<int, int>("t1", 1, 1);
            //    //tran.Insert<int, int>("t2", 1, 1);
            //    DBreeze.Diagnostic.SpeedStatistic.StartCounter("xp");
            //    //xp: 1; Time: 17472 ms;    with backup
            //    //xp: 1; Time: 10440 ms;    without backup
            //    for (int i = 0; i < 1000000; i++)
            //    {
            //        tran.Insert<int, int>("t1", i, i);
            //    }

            //    tran.Commit();

            //    DBreeze.Diagnostic.SpeedStatistic.PrintOut("xp", true);
            //}

            //using (var tran = engine.GetTransaction())
            //{
            //    //foreach (var row in tran.SelectBackward<int, int>("t1"))
            //    //{
            //    //    Console.WriteLine(row.Key);
            //    //}


            //    tran.Insert<int, int>("t1", 1, 1);
            //    //tran.InsertTable<int>("t1", 125, 0).Insert<int, int>(12, 1);
            //    tran.Commit();
            //}

            using (var tran = engine.GetTransaction())
            {
                for (int i = 0; i < 10; i++)
                {
                    tran.Insert<int, int>("t1", i, i);
                }

                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {
                //foreach (var row in tran.SelectBackwardFromTo<int, int>("t1",8,true,2,true))
                foreach (var row in tran.SelectForwardFromTo<int, int>("t1", 2, true, 8, true))
                {
                    Console.WriteLine("{0}", row.Key);
                }
            }
        }

        private void TestInsertPart1()
        {
            //using (var tran = engine.GetTransaction())
            //{
            //    var tbl = tran.InsertTable<int>("t1", 1, 0);
            //    tbl.Insert<string, byte[]>("4", null);
            //    tbl.Insert<string, byte[]>("2", null);

            //    //var tbl1 = tran.InsertTable<int>("t1", 1, 1);
            //    //tbl1.Insert<string, byte[]>("1", null);
            //    //tbl1.Insert<string, byte[]>("2", null);

            //    //var tbl2 = tran.InsertTable<int>("t1", 1, 2);
            //    //tbl2.Insert<string, byte[]>("1", null);
            //    //tbl2.Insert<string, byte[]>("2", null);

            //    tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 1, 2 }, 128);
            //    //tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 1, 2 }, 143);
            //    tran.Commit();
            //    //tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 1, 2 }, 150);
            //    //tran.Commit();
            //}

            using (var tran = engine.GetTransaction())
            {

                tran.InsertHashSet<int, string>("t1", 1, new HashSet<string> { "5" }, 0, true);
                //tran.InsertHashSet<int, string>("t1", 1, new HashSet<string> { "4", "2" }, 1, true);
                //tran.InsertHashSet<int, string>("t1", 1, new HashSet<string> { "4", "2" }, 2, true);
                //tran.InsertHashSet<int, string>("t1", 1, new HashSet<string> { "1", "2" }, 3, true);
                //tran.InsertHashSet<int, string>("t1", 1, new HashSet<string> { "1", "2" }, 4, true);

                tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 1, 2 }, 312);
                tran.InsertHashSet<int, string>("t1", 1, new HashSet<string> { "6" }, 1, true);
                tran.Commit();
            }

            using (var tran = engine.GetTransaction())
            {
                //foreach (var st in tran.SelectTable<int>("t1", 1, 0).SelectForward<string, byte[]>())
                //{
                //    Console.WriteLine(st.Key);
                //}
                //foreach (var st in tran.SelectTable<int>("t1", 1, 1).SelectForward<string, byte[]>())
                //{
                //    Console.WriteLine(st.Key);
                //}

                foreach (var st in tran.SelectTable<int>("t1", 1, 0).SelectHashSet<string>())
                {
                    Console.WriteLine(st);
                }
                foreach (var st in tran.SelectTable<int>("t1", 1, 1).SelectHashSet<string>())
                {
                    Console.WriteLine(st);
                }
                foreach (var st in tran.SelectTable<int>("t1", 1, 2).SelectHashSet<string>())
                {
                    Console.WriteLine(st);
                }
                //foreach (var st in tran.SelectTable<int>("t1", 1, 3).SelectHashSet<string>())
                //{
                //    Console.WriteLine(st);
                //}
                //foreach (var st in tran.SelectTable<int>("t1", 1, 4).SelectHashSet<string>())
                //{
                //    Console.WriteLine(st);
                //}
            }

            //using (var tran = engine.GetTransaction())
            //{

            //    tran.InsertHashSet<int, string>("t1", 1, new HashSet<string> { "5", "2" }, 0, true);
            //    tran.InsertHashSet<int, string>("t1", 1, new HashSet<string> { "7", "4" }, 1, true);
            //    tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 1, 2 }, 128);

            //    tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 1, 2 }, 143);


            //    tran.Commit();

            //    tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 1, 2 }, 150);
            //    tran.Commit();
            //}

            //using (var tran = engine.GetTransaction())
            //{
            //    foreach (var st in tran.SelectTable<int>("t1", 1, 0).SelectHashSet<string>())
            //    {
            //        Console.WriteLine(st);
            //    }
            //    foreach (var st in tran.SelectTable<int>("t1", 1, 1).SelectHashSet<string>())
            //    {
            //        Console.WriteLine(st);
            //    }
            //}




            //tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 1, 2, 3 }, 10);
            //tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 1, 2, 3 }, 20);
            //tran.Commit();

            //tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 2, 3, 4 }, 10);
            //tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 2, 3, 4 }, 20);
            //tran.Commit();

            //using (var tran = engine.GetTransaction())
            //{
            //    tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 2, 3, 4 }, 10);
            //    tran.Commit();
            //}
        }

        private void TestSelectDataBlock_MT()
        {
            TestSelectDataBlock_MT1();
            return;




            Action a = () =>
            {
                TestSelectDataBlock_MT1();
            };

            a.DoAsync();

            Action a1 = () =>
            {
                TestSelectDataBlock_MT2();
            };

            a1.DoAsync();
        }

        private void TestSelectDataBlock_MT1()
        {
            using (var tran = engine.GetTransaction())
            {
                byte[] res = null;
                byte[] ptr = null;

                //ptr = new byte[]{
                //        0 ,
                //        0,
                //        0,
                //        0,
                //        0,
                //        0,
                //        0,
                //        64,
                //        0,
                //        0,
                //        0,
                //        4,
                //        0,
                //        0,
                //        0,
                //        4};


                ////res = tran.SelectDataBlock("t1", ptr);

                //if (res != null)
                //{
                //    Console.WriteLine(System.Text.Encoding.ASCII.GetString(res));
                //}
                //else
                //{
                //    Console.WriteLine("ptr doesn't exist");
                //}

                //return;

                var row = tran.Select<int, byte[]>("t2", 1);
                if (row.Exists)
                {
                    res = tran.SelectDataBlock("t1", row.Value);
                    if (res != null)
                    {
                        Console.WriteLine(System.Text.Encoding.ASCII.GetString(res));
                    }
                    else
                    {
                        Console.WriteLine("ptr doesn't exist");
                    }
                }


                
                ptr = tran.InsertDataBlock("t1", ptr, System.Text.Encoding.ASCII.GetBytes("k3ku"));

                tran.Insert<int, byte[]>("t2", 1, ptr);

                tran.Commit();

                tran.InsertDataBlock("t1", ptr, System.Text.Encoding.ASCII.GetBytes("k4ku"));


                //tran.InsertDataBlock("t1", ptr, System.Text.Encoding.ASCII.GetBytes("k4ku"));

                //res = tran.SelectDataBlock("t1", ptr);

                //if (res != null)
                //{
                //    Console.WriteLine(System.Text.Encoding.ASCII.GetString(res));
                //}
                //else
                //{
                //    Console.WriteLine("ptr doesn't exist");
                //}

                //tran.Commit();

                //ptr = tran.InsertDataBlock("t1", ptr, System.Text.Encoding.UTF8.GetBytes("kudu"));

                //res = tran.SelectDataBlock("t1", ptr);

                //Console.WriteLine(System.Text.Encoding.UTF8.GetString(res));
            }
        }

        private void TestSelectDataBlock_MT2()
        {
            return;
            using (var tran = engine.GetTransaction())
            {

                byte[] ptr = tran.InsertDataBlock("t1", null, System.Text.Encoding.UTF8.GetBytes("kuku"));

                byte[] res = tran.SelectDataBlock("t1", ptr);

                Console.WriteLine(System.Text.Encoding.UTF8.GetString(res));

                ptr = tran.InsertDataBlock("t1", ptr, System.Text.Encoding.UTF8.GetBytes("kudu"));

                res = tran.SelectDataBlock("t1", ptr);

                Console.WriteLine(System.Text.Encoding.UTF8.GetString(res));
            }
        }

        private void TestSelectDataBlock()
        {
            using (var tran = engine.GetTransaction())
            {
                
                byte[] ptr = tran.InsertDataBlock("t1", null, System.Text.Encoding.UTF8.GetBytes("kuku"));
                                
                byte[] res = tran.SelectDataBlock("t1", ptr);

                Console.WriteLine(System.Text.Encoding.UTF8.GetString(res));

                ptr = tran.InsertDataBlock("t1", ptr, System.Text.Encoding.UTF8.GetBytes("kudu"));

                res = tran.SelectDataBlock("t1", ptr);

                Console.WriteLine(System.Text.Encoding.UTF8.GetString(res));
            }
        }

        private void TestLinkToValue()     
        {
            byte[] ptr=null;

            //NOTE
            //Reading from cash will try to make cache from the pointer and compare it with existing hashes in Dictionary, 
            //hash created fro 8 bytes will not correspondent with hash created from 5 bytes.
            //Only SelectDirect for now uses this ptr, so we can try to resize it to dbStorage.TrieSettings.POINTER_LENGHT; (first deleting leading 0)
            //and always return ptr with 8 byte

            //Made so, that ChangeKey, Insert and InsertPart (in master and nested tables) return ptr of 8 bytes, and in SelectDirect this pointer will receive size of the table pointer

            using (var tran = engine.GetTransaction())
            {
                //var row1 = tran.Select<int, byte[]>("t1", 1);
                //ptr = row1.LinkToValue.EnlargeByteArray_BigEndian(8); 

                for (int i = 1; i < 100; i++)
                {
                    tran.Insert<int, int>("t1", i, i, out ptr);
                }
                tran.Commit();
                
               // ptr = ptr.EnlargeByteArray_BigEndian(8);

                foreach (var row in tran.SelectForward<int, int>("t1"))
                {
                    var row1 = tran.SelectDirect<int, int>("t1", row.LinkToValue);
                    Console.WriteLine("K: {0}", row.Key);
                }

                //var row = tran.SelectDirect<int, byte[]>("t1", ptr);

                //if (row.Exists)
                //{
                //    Console.WriteLine("K: {0}", row.Key);
                //}

            }
        
        }


        private void TestRollbackV1()
        {

            //using (var tran = engine.GetTransaction())
            //{
            //    tran.Insert<int, int>("t1", 1, 1);
            //    tran.Commit();
            //}

            //using (var tran = engine.GetTransaction())
            //{
            //    tran.Insert<int, int>("t1", 1, 2);
            //    tran.Commit();
            //}

            //DateTime dt1 = DateTime.Now;
            //byte[] dst = dt1.To_8_bytes_array_BigEndian();
            ////byte[] data = null;
            ////byte[] hh = ((uint)data.Length).To_4_bytes_array_BigEndian();

            //byte[] bdt = new byte[] { 138, 213, 100, 98, 114, 101, 101, 122 };

            //DateTime dt = bdt.To_DateTime_BigEndian();

            //return;

            //using (var tran = engine.GetTransaction())
            //{
            //    try
            //    {
            //        //ulong c = tran.Count("Tro335/Fms145");  //828
            //        //ulong c = tran.Count("Tro416/Fms1");  //502
            //        ulong c = tran.Count("Tro416/Fms21");  //505
                    
            //    }
            //    catch (Exception ex)
            //    {
                    
            //    }
               
            //}

            //Console.WriteLine(engine.Scheme.GetTablePathFromTableName("t1"));
            //Console.WriteLine(engine.Scheme.GetTablePathFromTableName("t2"));


            //using (var tran = engine.GetTransaction())
            //{
                //tran.Insert<byte[], int>("t1", new byte[] { 1 }, 1);
                //tran.Insert<byte[], int>("t1", new byte[] { 2 }, 1);
                //tran.Insert<byte[], int>("t1", new byte[] { 3 }, 1);
                //tran.Insert<byte[], int>("t1", new byte[] { 4 }, 1);
                //tran.Insert<byte[], int>("t1", new byte[] { 5 }, 1);
                //tran.Insert<byte[], int>("t1", new byte[] { 6 }, 1);

                //tran.RemoveKey<byte[]>("t1", new byte[] { 5 });

                //tran.Commit();
            //}

            //using (var tran = engine.GetTransaction())
            //{
            //    tran.Insert<int, int>("t1", 1, 2);
            //    tran.Insert<int, int>("t1", 2, 1);
            //    tran.Insert<int, int>("t1", 3, 1);

            //    tran.Insert<int, int>("t2", 1, 2);
            //    tran.Insert<int, int>("t2", 2, 1);
            //    tran.Insert<int, int>("t2", 3, 1);

            //    tran.Commit();
            //}

            //using (var tran = engine.GetTransaction())
            //{
            //    Console.WriteLine("t1");
            //    foreach (var row in tran.SelectForward<byte[], int>("t1"))
            //    {
            //        Console.WriteLine("Key: {0}; Value: {1}", row.Key.ToBytesString(""), row.Value);
            //    }

            //    Console.WriteLine("t2");
            //    foreach (var row in tran.SelectForward<byte[], int>("t2"))
            //    {
            //        Console.WriteLine("Key: {0}; Value: {1}", row.Key.ToBytesString(""), row.Value);
            //    }
            //}

            Console.WriteLine("Done");
        }

        private void testPartialUpdate()
        {
            using (var tran = engine.GetTransaction())
            {
                var row = tran.Select<int, byte[]>("t1", 1);
                Console.WriteLine("before");
                Console.WriteLine(row.Value.ToBytesString(""));

                //tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 1, 1 }, 0);

                //tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 1, 1,2,2,2 }, 0);
                tran.InsertPart<int, byte[]>("t1", 1, new byte[] { 7 }, 0);

                //tran.Insert<int, byte[]>("t1", 1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0 });

                row = tran.Select<int, byte[]>("t1", 1);
                Console.WriteLine("after");
                Console.WriteLine(row.Value.ToBytesString(""));

                tran.Commit();
            }

            //using (var tran = engine.GetTransaction())
            //{
            //    var row = tran.Select<int, byte[]>("t1", 1);

            //    Console.WriteLine(row.Value.ToBytesString(""));

            //}



        }


        private void testChar()
        {
            DBreeze.Diagnostic.SpeedStatistic.StartCounter("kk");

            byte[] ptr = new byte[] {0,0,0,0,123};
            ptr = new byte[] { 0, 0, 0, 0, 139 };

            using (var tran = engine.GetTransaction())
            {
                tran.Insert<DateTime, byte[]>("t1", DateTime.Now, new byte[] { 1, 1 });
                
                tran.Commit();

                //byte[] dt = "ds fsdlskdjflskdjflsk lsdkjflsdkjf sdlkfjs lsdjl sdlkfjsd klflsd lsdkjlsd lsdkfjs".To_AsciiBytes();

                //ulong kk = DBreeze.Utils.Hash.MurMurHash.MixedMurMurHash3(dt);
                //Console.WriteLine(kk.To_8_bytes_array_BigEndian().ToBytesString(""));

                //uint z = DBreeze.Utils.Hash.MurMurHash.MurmurHash3(dt);  //D09C9021
                //uint z1 = DBreeze.Utils.Hash.MurMurHash.MurmurHash3(dt,37); //E6452A4E
                //ulong l = ((ulong)z) << 32;
                //ulong l1 = ((ulong)z) << 32; //D0 9C 90 21 00
                ////byte[] bbb = l1.To_8_bytes_array_BigEndian();
                //Console.WriteLine(l1.To_8_bytes_array_BigEndian().ToBytesString(""));
                //l |= (ulong)z1; //14DC74D030A0843B
                //Console.WriteLine(l.To_8_bytes_array_BigEndian().ToBytesString(""));
                //Thread.Sleep(1);


                //tran.Insert<int, int>("t1", 1, 10);


                //tran.Insert<int, int>("t1", 12, 17, out ptr);

                //tran.SelectDirect<int, int>("t1", ptr).PrintOut();

                //tran.ChangeKey<int>("t1", 12, 15,out ptr);

                //tran.SelectDirect<int, int>("t1", ptr).PrintOut();


                //var tbl = tran.InsertTable<int>("t3", 15, 0);

                //tbl.Insert<int, int>(12, 17, out ptr);
                //tran.Commit();

                //var row = tran.SelectTable<int>("t3", 15, 0)
                //    .SelectDirect<int, int>(ptr);

                //row.PrintOut();

                //var row = tran.SelectDirect<int, int>("t2", ptr);

                //row.PrintOut();

                //tran.Commit();
            }

            DBreeze.Diagnostic.SpeedStatistic.PrintOut("kk", true);

            #region "ready test "

            //using (var tran = engine.GetTransaction())
            //{
            //    var row1 = tran.SelectDirect<int, int>("t2", ptr);

            //    row1.PrintOut();
            //}

                //tran.SelectTable<int>("t1", 0, 0)
                //   .Select<uint, uint>(1);

                //tran.InsertTable<int>("t1", 1, 2)
                //         .Insert<uint, uint>(1, 1);



                //foreach (var row in tran.SelectForward<int, byte[]>("t1"))
                //{
                //    var tbl = row.GetTable(1);

                //        if (!tbl.Select<uint, uint>(1).Exists)
                //        {
                //            Console.WriteLine("ne");
                //        }

                //        tbl.CloseTable();

                //        //var tbl = row.GetTable(0);
                //        //if (!tbl1.Select<uint, uint>(1).Exists)
                //        //{
                //        //    Console.WriteLine("ne");
                //        //}
                    

                //}

                //for (int i = 0; i<100000; i++)
                //for (int i = 0; i < 100000; i++)
                //{
                //    //tran.InsertTable<int>("t1", i, 0)
                //    //    .Insert<uint, uint>(1, 1);

                //    tran.InsertTable<int>("t1", i, 1)
                //        .Insert<uint, uint>(1, 1);

                //    //var tbl = tran.SelectTable<int>("t1", i, 0);

                //    //if (!tbl.Select<uint, uint>(1).Exists)
                //    //{
                //    //    Console.WriteLine("ne");
                //    //}
                //    //tbl.CloseTable();
                //    //if (!tran.SelectTable<int>("t1", i, 0)
                //    //    .Select<uint, uint>(1).Exists)
                //    //{
                //    //    Console.WriteLine("ne");
                //    //}
                //}

                //tran.Commit();

            //}

           

            //Console.WriteLine("done");


            //string text="Kuku";

            //byte[] btText = System.Text.Encoding.UTF8.GetBytes(text);

            //uint val = DBreeze.Utils.Hash.MurMurHash.MurmurHash3(btText);

            //Console.WriteLine(val);
            //return;
            //byte[] _b = new byte[300];

            
            //System.IO.MemoryStream ms = new MemoryStream();
            //ms.Write(_b, 0, _b.Length);

            ////ms.Seek(10, SeekOrigin.Begin);
            ////ms.WriteByte(1);

            //byte res=0;
            //int iRes = 0;

            //ms.Seek(0, SeekOrigin.Begin);

            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("kk");

            //for (int i = 0; i < 200000; i++)
            //{
            //    for (int j = 0; j < 256; j++)
            //    {
            //        res = _b[j];

            //        //ms.Seek(j, SeekOrigin.Begin);
            //        //iRes = ms.ReadByte();


            //        //ms.Seek(j, SeekOrigin.Begin);
            //        //ms.WriteByte((byte)j);
            //        //ms.Seek(j, SeekOrigin.Begin);
            //        //ms.Seek(j, SeekOrigin.Begin);
            //        //ms.Seek(j, SeekOrigin.Begin);
            //        //ms.Seek(j, SeekOrigin.Begin);
            //        //ms.Seek(j, SeekOrigin.Begin);
            //        //ms.ReadByte();
            //    }
            //}
            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("kk", true); 
            ////    ms.Seek(10, SeekOrigin.Begin);
            ////int f = ms.ReadByte();
            //ms.Dispose();
            //return;

            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("kk");

            //Dictionary<uint,string> _d=new Dictionary<uint,string>();
            //_d.Add(10, "Hello my friends");
            //_d.Add(11, "Sehr gut!");

            //Dictionary<uint, string> _b = null;

            //using (var tran = engine.GetTransaction())
            //{

            //    //byte[] refPtr = tran.Insert<int, int>("t1", 1, 1);
            //    byte[] refPtr = new byte[] { 0, 0, 0, 0, 79 };
            //    int v = tran.SelectDirect<int, int>("t1",refPtr);
            //}

            //using (var tran = engine.GetTransaction())
            //{

       


            //    //tran.InsertTable<int>("t1", 1, 1)
            //    //    .Insert<uint, string>(1, "Test1")
            //    //    .Insert<uint, string>(2, "Test2")
            //    //    .Insert<uint, string>(3, "Test3");

            //    //tran.Commit();


            //    ////foreach (var row in tran.SelectTable("t1", 1, 1))
            //    //foreach (var row in tran.SelectForward<int,byte[]>("t1"))
            //    //{
            //    //    foreach (var r1 in row.GetTable(1).SelectForward<uint, string>())
            //    //    {
            //    //        r1.PrintOut();
            //    //    }
            //    //}

            //    ////Insert into Master Table Row
            //    //tran.InsertDictionary<int, uint, string>("t1", 10, _d, 0,true);
                
            //    ////Insert into Nested Table Dictionary
            //    //tran.InsertTable<int>("t1",15,0)
            //    //    .InsertDictionary<int, uint, string>(10, _d, 0,true);

            //    //tran.Commit();

            //    ////Select from master table
            //    //_b = tran.SelectDictionary<int, uint, string>("t1", 10, 0);

            //    //_b = tran.SelectTable<int>("t1",15,0)
            //    //    .SelectDictionary<int, uint, string>(10, 0);


            //}

            //engine.Scheme.DeleteTable("t1");

            //long l = 4565843215;

            //for (int i = 0; i < 1000000; i++)
            //{
            //    l.To_8_bytes_array_BigEndian();
            //}

                //using (var tran = engine.GetTransaction())
                //{

                //    //var tbl = tran.InsertTable<int>("t1", 1, 0);

                //    //tbl.Insert<int, int>(1, 1);
                //    //tbl.Insert<int, int>(3, 3);
                //    //var intTbl = tbl.GetTable<int>(2, 0);

                //    //intTbl.Insert<int, string>(1, "dd1")
                //    //    .Insert<int, string>(2, "dd2");


                //    //tbl = tran.InsertTable<int>("t1", 2, 0);

                //    //tbl.Insert<int, int>(1, 1);
                //    //tbl.Insert<int, int>(2, 2);
                //    //tbl.Insert<int, int>(3, 3);

                //    //tran.Commit();


                //    //foreach (var row in tran.SelectForward<int, byte[]>("t1"))
                //    //{
                //    //    Console.WriteLine("**********");
                //    //    foreach (var ir in row.GetTable(0).SelectForward<int,int>())
                //    //    {
                //    //        Console.WriteLine("***sub 1*******");
                //    //        if (ir.Key != 2)
                //    //        {
                //    //            ir.PrintOut();
                //    //        }
                //    //        else
                //    //        {
                //    //            Console.WriteLine("*****int***");
                //    //            foreach (var ir1 in ir.GetTable(0).SelectForward<int, string>())
                //    //            {
                //    //                ir1.PrintOut();
                //    //            }
                //    //        }
                //    //    }
                //    //}

                //    //var rw = tran.SelectTable<int>("t1",1,0)
                //    //         .GetTable<

                //    //foreach (var row in tran.SelectForward<int, byte[]>("t1"))
                //    //{
                //    //    Console.WriteLine("**********");
                //    //    foreach (var ir in row.GetTable(0).SelectForward<int, int>())
                //    //    {
                //    //        ir.PrintOut();
                //    //    }
                //    //}

                //    //////tran.Insert<int, int>("t1", 1, 1);

                //    //////tran.Insert<int, int>("t1", 2, 1);


                //    //////tran.Commit();


                //    //////tran.RemoveAllKeys("t1", false);

                //    //////foreach (var row in tran.SelectBackward<int, int>("t1"))
                //    //////{
                //    //////    row.PrintOut();
                //    //////}


                //    ////////tran.InsertTable<int>("t1", 1, 0).GetTable<int>(1, 0).GetTable<int>(1, 0).Insert<int, string>(15, "kuku");
                //    ////////tran.Commit();

                //    ////////tran.SelectTable<int>("t1", 1, 0).GetTable<int>(1, 0).GetTable<int>(1, 0).Select<int, string>(15).PrintOut();

                //    ////////byte[] ptrToValue = tran.Insert<int, byte[]>("t1", 1, new byte[] { 1, 2, 3,4,5,6 });

                //    ////////byte[] res = tran.SelectDirect<int, byte[]>("t1", ptrToValue);

                //    ////////Console.WriteLine(res.ToBytesString(""));



                //}



            //byte[] ba = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

            //byte[] rb = ba.Substring(2, 5);

        

            //Action a = null;

            //for (int i = 0; i < 1000000; i++)
            //{
            //    //a= ()=>{                    
            //    //    return;
            //    //};

            //    //a.DoAsync();

            //    rb = ba.Substring(2, 5);
            //}


                //using (var tran = engine.GetTransaction())
                //{
                //    tran.Insert<int, int>("t1", 1, 1);

                //    tran.Insert<int, int>("t1", 2, 1);


                //    tran.Commit();


                //    tran.RemoveAllKeys("t1",false);

                //    foreach (var row in tran.SelectBackward<int, int>("t1"))
                //    {
                //        row.PrintOut();
                //    }


                //    //tran.InsertTable<int>("t1", 1, 0).GetTable<int>(1, 0).GetTable<int>(1, 0).Insert<int, string>(15, "kuku");
                //    //tran.Commit();

                //    //tran.SelectTable<int>("t1", 1, 0).GetTable<int>(1, 0).GetTable<int>(1, 0).Select<int, string>(15).PrintOut();

                //    //byte[] ptrToValue = tran.Insert<int, byte[]>("t1", 1, new byte[] { 1, 2, 3,4,5,6 });

                //    //byte[] res = tran.SelectDirect<int, byte[]>("t1", ptrToValue);

                //    //Console.WriteLine(res.ToBytesString(""));



                //}

            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("kk", true); 

            #endregion
        }


        #region "TEST DbInTable"

        private void TEST_DBINTABLE_MT_START()
        {
            Action a = () =>
            {
                TEST_DBINTABLE_T1();
            };

            a.DoAsync();

            Action a1 = () =>
            {
                TEST_DBINTABLE_T2();
            };

            a1.DoAsync();
        }


        private void TEST_DBINTABLE_T1()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {

                    //var tbl = tran.InsertTable<int>("t1", 18, 0);

                    //byte[] dbp = tbl.InsertDataBlock(null, new byte[] { 1, 2, 3 });

                    //tbl.InsertPart<int, byte[]>(19, dbp, 10);

                    //tran.Commit();

                    //tbl.CloseTable();


                    //tbl = tran.SelectTable<int>("t1", 18, 0);
                    //var row = tbl.Select<int, byte[]>(19);
                    //byte[] fr = row.GetDataBlock(10);

                    //if (fr == null)
                    //    Console.WriteLine("T1 NULL");
                    //else
                    //    Console.WriteLine("T1 " + fr.ToBytesString());

                    var row = tran.Select<int, byte[]>("t1", 17);
                    byte[] dataBlock = row.GetDataBlock(58);

                    dataBlock = tran.InsertDataBlock("t1", dataBlock, new byte[] {7, 7 });

                    tran.InsertPart<int, byte[]>("t1", 17, dataBlock, 58);

                    tran.Commit();

                    var row1 = tran.Select<int, byte[]>("t1", 17);
                    byte[] fr = row1.GetDataBlock(58);

                    if (fr == null)
                        Console.WriteLine("T1 NULL");
                    else
                        Console.WriteLine("T1 " + fr.ToBytesString());


                    fr = row.GetDataBlock(10);

                    if (fr == null)
                        Console.WriteLine("T1 NULL");
                    else
                        Console.WriteLine("T1 " + fr.ToBytesString());


                    //byte[] dataBlock = tran.InsertDataBlock("t1", null, new byte[] { 1, 2, 3 });

                    //tran.InsertPart<int, byte[]>("t1", 17, dataBlock, 10);
                    
                    //tran.Commit();

                    //dataBlock = tran.InsertDataBlock("t1", null, new byte[] { 4, 5, 6 });
                    //byte[] dataBlock = tran.InsertDataBlock("t1", null, new byte[] { 7, 4,5 });

                    //tran.InsertPart<int, byte[]>("t1", 17, dataBlock, 10);

                    //Thread.Sleep(2000);

                    //tran.Commit();

                    //var row = tran.Select<int, byte[]>("t1", 17);
                    //byte[] res = row.GetDataBlock(10);

                    //Thread.Sleep(2000);

                    //tran.Commit();

                   
                   
                    
                    #region "READY TESTs"
                    //Thread.Sleep(1000);

                    //tran.Insert<int, byte[]>("t1", 1, new byte[] {1});

                    //var row = tran.Select<int, byte[]>("t1", 1);

                    //byte[] t = row.Value;

                    //if (t == null)
                    //    Console.WriteLine("T1 NULL");
                    //else
                    //    Console.WriteLine("T1 " + t.ToBytesString());

                    //Thread.Sleep(3000);
                    //tran.Commit();

                    //tran.InsertDictionary<int, int>("t1", new Dictionary<int, int>(), false);
                    //tran.InsertTable<int>("t1", 1, 0).InsertDictionary<uint, uint>(new Dictionary<uint, uint>(), false);

                    //tran.SelectDictionary<int, int>("t1");
                    //tran.SelectTable<int>("t1", 1, 0).SelectDictionary<uint, uint>();

                    //TEST 3
                    //int v = tran.SelectDirect<int, int>("t1", refPtr);
                    //Console.WriteLine(v);

                    //byte[] refPtr = tran.Insert<int, int>("t1", 1, 11);

                    //byte[] refPtr1 = new byte[] { 0, 0, 0, 0, 0x40 };
                    //int v = tran.SelectDirect<int, int>("t1", refPtr1);

                    //Console.WriteLine(refPtr.ToBytesString(""));
                    //Console.WriteLine(v);

                    //tran.InsertTable<int>("t1", 1, 0)
                    //    .GetTable<int>(2, 0)
                    //    .Insert<int, string>(1, "e")
                    //    .Insert<int, string>(2, "e")
                    //    ;

                    ////tran.InsertTable<int>("t1", 1, 0)
                    ////  .GetTable<int>(2, 0)
                    ////  .Insert<int, string>(1, "e")
                    ////  .Insert<int, string>(2, "e")
                    ////  ;

                    //Thread.Sleep(2000);
                    //tran.Commit();


                    //tran.InsertTable<int>("t1", 1, 0)
                    //    .Insert<int, string>(10, "str10")
                    //    .Insert(11, "str11");

                    //foreach (var row in tran
                    //                    .SelectTable<int>("t1", 1, 0)
                    //                    .SelectForward<int, string>()
                    //                    )
                    //{
                    //    row.PrintOut("T1");
                    //}

                    //tran.Insert("t1", 1, "sdf");
                    //tran.InsertTable("t1", 1, "string");


                    //tran.InsertTable<int>("t3", 1, 0)
                    //    .GetTable<int>(1, 0)
                    //    .Insert<int, string>(1, "Hi3");

                    //tran.InsertTable<int>("t3", 1, 0)
                    //    .GetTable<int>(1, 0)
                    //    .ChangeKey<int>(1, 2);

                    //tran.InsertTable<int>("t3", 1, 0)
                    //    .ChangeKey(1, 2)
                    //    .GetTable<int>(2, 0)
                    //    .Insert<int, string>(2, "Hi33");

                    //tran.SelectTable<int>("t3", 1, 0)
                    //   .GetTable<int>(2, 0)
                    //   .Select<int, string>(2)
                    //   .PrintOut("T1");

                    //var row = tran.SelectTable<int>("t3", 1, 0)
                    // .GetTable<int>(2, 0)
                    // .Select<int, string>(2);
                    // //.PrintOut("T1");

                    //row.GetTable().Select<int, string>(2)
                    //    .PrintOut("T1");


                    //Thread.Sleep(2000);
                    //tran.Commit();




                    ///////////////////////////////////////

                    //DBreeze.LianaTrie.NestedTableStorage df = new DBreeze.LianaTrie.NestedTableStorage(null,null);

                    //tran.Select<int, string>("t1", 1).PrintOut("T1");
                    //tran.Insert<int, string>("t1", 1, "Hs54654645464564545645");
                    //tran.Insert<int, string>("t1", 1, "Hs12312555464x");
                    //tran.Select<int, string>("t1", 1).PrintOut("T1");

                    //TEST 2
                    //tran.SelectTable<int>("t1", 1, 0)
                    // .Select<int, string>(1)
                    // .PrintOut();

                    //tran.InsertTable<int>("t3", 1, 0)
                    //    .GetTable<int>(1, 0)
                    //    .Insert<int, string>(1, "Hi3");

                    //tran.InsertTable<int>("t1", 1, 0)
                    //    .Insert<int, string>(1, "T1 sdsdsds234");

                    //tran.InsertTable<int>("t2", 1, 0)
                    //    .Insert<int, string>(1, "T2 jkh dsfsdf234");

                    //tran.SelectTable<int>("t1", 1, 0)
                    //   .Select<int, string>(1)
                    //   .PrintOut("T1");

                    //tran.SelectTable<int>("t2", 1, 0)
                    // .Select<int, string>(1)
                    // .PrintOut("T1");

                    //tran.SelectTable<int>("t3", 1, 0)
                    //   .GetTable<int>(1, 0)
                    //   .Select<int, string>(1)
                    //   .PrintOut("T1");

                    //Thread.Sleep(2000);
                    //tran.Commit();

                    //tran.SelectTable<int>("t1", 1, 0)
                    //  .Select<int, string>(1)
                    //  .PrintOut();


                    //TEST 1
                    //tran.Insert<int, int>("t1", 1, 1);

                    //var row = tran.Select<int, int>("t1", 1);

                    //if (row.Exists)
                    //{
                    //    Console.WriteLine("t1 " + row.Value);
                    //}
                    //else
                    //{
                    //    Console.WriteLine("t1 not");
                    //}
                    //tran.Commit();
                    //Thread.Sleep(2000);

                    //TEST
                    //tran.Insert<int,int>("t1",1,3);

                    //var row = tran.Select<int, int>("t1", 1);

                    //if (row.Exists)
                    //{
                    //    Console.WriteLine("t1 " + row.Value);
                    //}
                    //else
                    //{
                    //    Console.WriteLine("t1 not");
                    //}

                    //Thread.Sleep(2000);
                    ////tran.Commit();
                    //Console.WriteLine("t1 is done");
                    #endregion

                    Console.WriteLine("t1 is done");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }
        }

        private void TEST_DBINTABLE_T2()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    //Thread.Sleep(1000);

                    //var row = tran.Select<int, byte[]>("t1", 17);
                    //byte[] res = row.GetDataBlock(10);

                    //if(res == null)
                    //    Console.WriteLine("T2 NULL");
                    //else
                    //    Console.WriteLine("T2 " + res.ToBytesString());

                    //Thread.Sleep(2000);

                    //row = tran.Select<int, byte[]>("t1", 17);
                    //res = row.GetDataBlock(10);

                    //if (res == null)
                    //    Console.WriteLine("T2 NULL");
                    //else
                    //    Console.WriteLine("T2 " + res.ToBytesString());



                    //var row = tran.Select<int, byte[]>("t1", 1);

                    //byte[] t = row.Value;

                    //if (t == null)
                    //    Console.WriteLine("T2 NULL");
                    //else
                    //    Console.WriteLine("T2 " + t.ToBytesString());

                    //Thread.Sleep(5000);

                    //row = tran.Select<int, byte[]>("t1", 1);

                    //t = row.Value;

                    //if (t == null)
                    //    Console.WriteLine("T2 NULL");
                    //else
                    //    Console.WriteLine("T2 " + t.ToBytesString());

                    //Thread.Sleep(300);
                    //byte[] refPtr = new byte[] { 0, 0, 0, 0, 0x40 };
                    //int v = tran.SelectDirect<int, int>("t1", refPtr);

                    //Console.WriteLine(v);

                    //Thread.Sleep(100);

                    //tran.SelectTable<int>("t1", 1, 0);

                    //foreach (var row in tran.SelectTable<int>("t1", 1, 0).SelectForward<int, byte[]>())
                    //{

                    //    foreach (var r1 in row.GetTable(0).SelectForward<int, string>())
                    //    {
                    //        r1.PrintOut();
                    //    }

                    //    //row.PrintOut();
                    //}

                    //Console.WriteLine("dsf");

                    //Thread.Sleep(3000);

                    //foreach (var row in tran.SelectTable<int>("t1", 1, 0).SelectForward<int, byte[]>())
                    //{

                    //    foreach (var r1 in row.GetTable(0).SelectForward<int, string>())
                    //    {
                    //        r1.PrintOut();
                    //    }

                    //    //row.PrintOut();
                    //}





                  

                    #region "READY TESTs"

                    //TEST 3
                    //Thread.Sleep(1000);

                    //foreach (var row in tran.SelectTable<int>("t1", 1, 0).SelectForward<int, string>())
                    //{
                    //    row.PrintOut("T2");
                    //}

                    //tran.Select<int, string>("t1", 1).PrintOut();

                    //tran.SelectTable<int>("t3", 1, 0)
                    //   .GetTable<int>(2, 0)
                    //   .Select<int, string>(2)
                    //   .PrintOut("T2");

                    //tran.SelectTable<int>("t3", 1, 0)
                    //  .GetTable<int>(1, 0)
                    //  .Select<int, string>(1)
                    //  .PrintOut("T2");


                    ///////////////////////////////////////

                    // Console.WriteLine("t2 " + System.Threading.Thread.CurrentThread.ManagedThreadId);

                    //TEST 2
                    //Thread.Sleep(1000);

                    //tran.Select<int, string>("t1", 1).PrintOut("T2");

                    // tran.SelectTable<int>("t1", 1, 0)
                    //   .Select<int, string>(1)
                    //   .PrintOut("T2");

                    // tran.SelectTable<int>("t2", 1, 0)
                    //.Select<int, string>(1)
                    //.PrintOut("T2");

                    // tran.SelectTable<int>("t3", 1, 0)
                    //   .GetTable<int>(1, 0)
                    //   .Select<int, string>(1)
                    //   .PrintOut("T2");

                    //TEST 1
                    //Thread.Sleep(1000);

                    //var row = tran.Select<int, int>("t1", 1);

                    //if (row.Exists)
                    //{
                    //    Console.WriteLine("t2 " + row.Value);
                    //}
                    //else
                    //{
                    //    Console.WriteLine("t2 not");
                    //}

                    //TEST
                    //Thread.Sleep(1000);
                    //var row = tran.Select<int, int>("t1", 1);

                    //if (row.Exists)
                    //{
                    //    Console.WriteLine("t2 " + row.Value);
                    //}
                    //else
                    //{
                    //    Console.WriteLine("t2 not");
                    //}
                    #endregion
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T2 YAHOO! " + ex.ToString());
                }
            }
        }

        private void TEST_DBINTABLE()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    /**************************************************   FOR DOCU *******************************************/
                    //Reserve first for holding names, then user must know about it

                    /*Table "t1"
                     * Key  | Value
                        1   |  /...64 byte..../                             /...64 byte..../                                /...64 byte..../
                     *          Key<int>Value                                 Key<string>     Value
                     *           1     /...64 byte..../                      a5          /...64 byte..../ /...64 byte..../
                     *           2     /...64 byte....//...64 byte..../      b6          string
                     *           3                                           t7          int                     
                     *                                                       h8          long
                        2   |  /...64 byte..../
                        3   |  /...64 byte....//...64 byte....//...64 byte....//...64 byte..../
                     */



                    //tran
                    //    .InsertTable<int>("t1", 1, 0)
                    //    .Insert<int, string>(12, "kuku");

                    //var row = tran
                    //    .SelectTable<int>("t1", 1, 0)
                    //    .Select<int, string>(12);


                    //if (row.Exists)
                    //{
                    //    Console.WriteLine(row.Value);
                    //}

                    //tran.Commit();

                    /*************************************************************************************************************/

                    





                    //tran
                    //    .InsertTable<int>("t1", 1, 0)
                    //    .Insert<int, int>(1, 1);

                    //tran
                    //    .InsertTable<int>("t1", 2, 0)
                    //    .Insert<int, int>(1, 1);

                    //tran
                    //    .InsertTable<int>("t1", 2, 0)
                    //    .Insert<int, int>(11, 15)
                    //    .Insert<int, int>(12, 17);

                    //tran
                    //    .InsertTable<int>("t1", 1, 1)
                    //    .Insert<int, int>(2, 2);


                    //tran
                    //    .InsertTable<int>("t1", 3, 0)
                    //    .GetTable<int>(1, 0)
                    //    .Insert<int, int>(12, 17);

                    //tran
                    //    .InsertTable<int>("t1", 4, 0)
                    //    .GetTable<int>(1, 0)
                    //    .Insert<int, int>(12, 17);

                    //tran
                    //    .InsertTable<int>("t1", 6, 0)
                    //    .GetTable<int>(1, 0)
                    //    .GetTable<int>(1, 0)
                    //    .Insert<int, int>(18, 17);

                    //////////////////////////////////////////////

                    //tran
                    //   .SelectTable<int>("t1", 1, 0)
                    //   .Select<int, int>(1)
                    //   .PrintOut();

                    //tran
                    //    .SelectTable<int>("t1", 2, 0)
                    //    .Select<int, int>(11)
                    //    .PrintOut();

                    //tran
                    //    .SelectTable<int>("t1", 2, 0)
                    //    .Select<int, int>(12)
                    //    .PrintOut();

                    //tran
                    //  .SelectTable<int>("t1", 1, 1)
                    //  .Select<int, int>(2)
                    //  .PrintOut();

                    //tran
                    //   .SelectTable<int>("t1", 3, 0)
                    //   .GetTable<int>(1, 0)
                    //   .Select<int, int>(12)
                    //   .PrintOut();

                    //tran
                    //   .SelectTable<int>("t1", 6, 0)
                    //   .GetTable<int>(1, 0)
                    //   .GetTable<int>(1, 0)
                    //   .Select<int, int>(18)
                    //   .PrintOut();

                    //tran.Commit();




                    //row = tran
                    //    .SelectTable<int>("t1", 1, 0)
                    //    .Select<int, int>(1);

                    //if (row.Exists)
                    //{
                    //    Console.WriteLine(row.Value);
                    //}
                    //else
                    //{

                    //}

                   

                        //DBreeze.Diagnostic.SpeedStatistic.StartCounter("x");
                    //    DBreeze.Diagnostic.SpeedStatistic.StopCounter("x");
                    //DBreeze.Diagnostic.SpeedStatistic.PrintOut("x");
                    //DBreeze.Diagnostic.SpeedStatistic.ClearAll();

                   

                    //DbInTable t = tran.InsertTable<int>("t1", 1, 0);

                    //var row = t.Select<long, string>(10);

                    //if (row.Exists)
                    //{
                    //    Console.WriteLine(row.Value);
                    //}

                    //row = t.Select<long, string>(11);

                    //if (row.Exists)
                    //{
                    //    Console.WriteLine(row.Value);
                    //}

                    //row = t.Select<long, string>(12);

                    //if (row.Exists)
                    //{
                    //    Console.WriteLine(row.Value);
                    //}




                    //DbInTable xdit = tran.InsertTable<int>("t1", 100, 0);
                                      

                   // DbInTable xditChild1 = xdit.InsertTable<int>(9, 0);

                   // xditChild1.Insert<int, int>(5, 17);

                   // xditChild1.Commit();
                   // xdit.Commit();

                   // var irow_c1 = xditChild1.Select<int, int>(5);
                   // int vl_c1 = 0;
                   // if (irow_c1.Exists)
                   // {
                   //     vl_c1 = irow_c1.Value;
                   // }
                   // else
                   // {

                   // }




                   // DbInTable dit = tran.InsertTable<int>("t1", 1, 0);

                   // dit.Insert<int, int>(1, 2);

                   // dit.Commit();

                   // var irow = dit.Select<int, int>(1);
                   // int vl = 0;
                   // if (irow.Exists)
                   // {
                   //     vl = irow.Value;
                   // }
                   // else
                   // {

                   // }




                   // DbInTable dit1 = tran.InsertTable<int>("t1", 2, 0);

                   // dit1.Insert<int, int>(1, 2);

                   // dit1.Commit();

                   // var irow1 = dit1.Select<int, int>(1);
                   // int vl1 = 0;
                   // if (irow1.Exists)
                   // {
                   //     vl1 = irow1.Value;
                   // }
                   // else
                   // {

                   // }

                   // ////Here we will have problem that we have to move dit on the other place, cause its value will grow

                   // DbInTable dit2 = tran.InsertTable<int>("t1", 1, 1);

                   // dit2.Insert<int, int>(1, 2);

                   // dit2.Commit();

                   // var irow2 = dit2.Select<int, int>(1);
                   // int vl2 = 0;
                   // if (irow2.Exists)
                   // {
                   //     vl2 = irow2.Value;
                   // }
                   // else
                   // {

                   // }





                   //tran.Commit();

                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }
        }

        #endregion

        #region "TEST STARTS_WITH"

        private void TEST_START_WITH_insert1(DBreeze.Transactions.Transaction tran, string tn)
        {
            tran.Insert<int, int>(tn, 1, 1);
            tran.Commit();
        }
        private void TEST_START_WITH_insert(DBreeze.Transactions.Transaction tran, int tro,int din)
        {
            string tn = "Tro" + tro.ToString() + "/Din" + din.ToString() + "/Year";
            tran.Insert<int, int>(tn, 1, 1);
            tran.Commit();
        }

        private void TEST_START_WITH()
        {

            List<string> rt = engine.Scheme.GetUserTableNamesStartingWith("Tro" + 54 + "/Din" + 1 + "/Year");

            foreach (var rrr in rt)
            {
                Console.WriteLine(rrr);
            }

            return;

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    #region "TBL CREATE"
                    //TEST_START_WITH_insert1(tran,"Tro1/Din1/Year2009/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro1/Din1/Year2010/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro10/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro10/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro10/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro11/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro11/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro12/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro12/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro12/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro12/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro13/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro13/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro13/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro13/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro17/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro17/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro17/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro17/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro19/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro19/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro19/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro19/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro2/Din1/Year2009/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro2/Din1/Year2010/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro20/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro21/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro22/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro25/Din1/Year2008/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro26/Din1/Year2008/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro27/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro27/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro27/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro27/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro28/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro28/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro28/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro29/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro29/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro29/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro3/Din1/Year2009/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro3/Din1/Year2010/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro30/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro30/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro30/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro31/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro31/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro31/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro32/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro32/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro32/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro35/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro37/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro38/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro39/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro4/Din1/Year2009/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro4/Din1/Year2010/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro40/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro41/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro42/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro43/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro44/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro45/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro46/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro46/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro47/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro47/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro47/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro47/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro48/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro48/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro48/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro48/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro49/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro49/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro49/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro49/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro5/Din1/Year2009/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro5/Din1/Year2010/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro50/Din1/Year1997/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro50/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro50/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro50/Din2/Year1997/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro50/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro50/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro51/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro51/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro51/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro51/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro52/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro52/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro52/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro52/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro53/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro53/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro53/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro53/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro54/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro54/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro54/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro55/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro55/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro55/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro56/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro56/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro56/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro57/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro57/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro57/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro58/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro58/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro58/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro59/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro59/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro59/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro6/Din1/Year2009/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro6/Din1/Year2010/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro60/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro60/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro60/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro61/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro61/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro61/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro62/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro62/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro62/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro63/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro63/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro63/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro64/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro64/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro64/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro65/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro65/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro65/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro66/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro66/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro66/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro67/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro67/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro68/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro68/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro68/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro7/Din1/Year2009/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro7/Din1/Year2010/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro73/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro73/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro73/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro74/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro74/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro74/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro75/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro75/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro75/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro76/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro76/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro8/Din1/Year2009/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro8/Din1/Year2010/RelativeHours  ");
                    //TEST_START_WITH_insert1(tran,"Tro82/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro82/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro83/Din1/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro83/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro83/Din2/Year2009/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro83/Din2/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran,"Tro84/Din1/Year2010/RelativeHours ");
                    //TEST_START_WITH_insert1(tran, "Tro84/Din2/Year2010/RelativeHours ");
                    #endregion




                    //TEST_START_WITH_insert(tran, 12, 1);
                   //TEST_START_WITH_insert(tran, 12, 2);
                   //TEST_START_WITH_insert(tran, 12, 7);
                   //TEST_START_WITH_insert(tran, 13, 1);
                   //TEST_START_WITH_insert(tran, 1, 1);
                   //TEST_START_WITH_insert(tran, 124, 1);
                   //TEST_START_WITH_insert(tran, 1, 2);
                   //TEST_START_WITH_insert(tran, 124, 4);
                   //TEST_START_WITH_insert(tran, 16, 1);
                   //TEST_START_WITH_insert(tran, 16, 2);
                   //TEST_START_WITH_insert(tran, 17, 1);
                   //TEST_START_WITH_insert(tran, 17, 2);
                   //TEST_START_WITH_insert(tran, 12, 1);

                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }
        }

        #endregion

        #region "TEST VIRT ROLLBACK"

        private void TEST_VIRT_ROLLBACK()
        {
            //engine.Scheme.DeleteTable("t1");

            //using (var tran = engine.GetTransaction())
            //{
            //    try
            //    {

            //        byte[] p0 = new byte[] { 1, 1 };
            //        byte[] p1 = new byte[] { 1, 1, 1 };
            //        byte[] p2 = new byte[] { 1, 1, 2 };
            //        byte[] p3 = new byte[] { 1, 2 };



            //        tran.Insert<byte[], int>("t1", p1, 1);
            //        tran.Insert<byte[], int>("t1", p2, 1);
            //        tran.Insert<byte[], int>("t1", p3, 1);
            //        tran.Insert<byte[], int>("t1", p0, 1);
            //        tran.Commit();

            //        tran.RemoveKey<byte[]>("t1", p0);
            //        tran.Commit();
            //        tran.RemoveKey<byte[]>("t1", p1);
            //        tran.Commit();
            //        tran.RemoveKey<byte[]>("t1", p2);
            //        tran.Commit();

            //        var xr = tran.Select<byte[], int>("t1", p0);

            //        tran.Insert<byte[], int>("t1", p0, 1);
            //        tran.Commit();
            //        tran.Insert<byte[], int>("t1", p1, 1);
            //        tran.Commit();
            //        tran.Insert<byte[], int>("t1", p2, 1);
            //        tran.Commit();

            //        xr = tran.Select<byte[], int>("t1", p0);

            //        foreach (var row in tran.SelectForward<byte[], int>("t1"))
            //        {
            //            Console.WriteLine("1 K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
            //        }

            //        foreach (var row in tran.SelectBackward<byte[], int>("t1"))
            //        {
            //            Console.WriteLine("1 K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
            //        }

            //    }
            //    catch (Exception ex)
            //    {
            //        Console.WriteLine("T1 YAHOO! " + ex.ToString());
            //    }
            //}
            //return;

            engine.Scheme.DeleteTable("t1");
            engine.Scheme.DeleteTable("t2");

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    Console.WriteLine("1_t1 cnt: {0}", tran.Count("t1"));
                    Console.WriteLine("1_t2 cnt: {0}", tran.Count("t2"));

                    int cnt = 0;
                    for (int i = 0; i < 10000; i += 500)
                    {
                        cnt++;

                        tran.Insert<int, int>("t1", i, 1);
                        tran.Insert<int, int>("t2", i, 1);
                    }

                    tran.Commit();

                    Console.WriteLine(cnt);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    Console.WriteLine("2_t1 cnt: {0}", tran.Count("t1"));
                    Console.WriteLine("2_t2 cnt: {0}", tran.Count("t2"));

                    int cnt = 0;
                    for (int i = 0; i < 10000; i += 500)
                    {
                        cnt++;

                        tran.Insert<int, int>("t1", i, 234534);
                        tran.Insert<int, int>("t2", i, 7987);
                    }

                    //NO COMMIT
                    //tran.Commit();

                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    Console.WriteLine("3_t1 cnt: {0}", tran.Count("t1"));
                    Console.WriteLine("3_t2 cnt: {0}", tran.Count("t2"));

                    foreach (var row in tran.SelectForward<int, int>("t1"))
                    {
                        Console.WriteLine("1 K: {0}; V: {1}", row.Key, row.Value);
                    }

                    foreach (var row in tran.SelectForward<int, int>("t2"))
                    {
                        Console.WriteLine("2 K: {0}; V: {1}", row.Key, row.Value);
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }
        }

        #endregion


        #region "TEST SpinLock"

        private void TEST_SPIN_START_TH(int totalQ)
        {
            //System.Threading.Thread tr = new Thread(new ThreadStart(TEST_SPIN_IncrementValue));
            //tr.Start();

            Action a = () =>
                {
                    TEST_SPIN_IncrementValue(totalQ);
                };

            a.DoAsync();
        }

        private void TEST_SPIN_START()
        {
            valToIncr = 0;
            sharedCnt = 0;
            int totalQ = 100000;

            DBreeze.Diagnostic.SpeedStatistic.StartCounter("i");
            for (int i = 0; i < totalQ; i++)
            {
                TEST_SPIN_START_TH(totalQ);
            }
        }

        int valToIncr = 0;
        int sharedCnt = 0;
        DbReaderWriterSpinLock sp = new DbReaderWriterSpinLock();   //1K -> 18-28 ms; 10 K -> 220 - 260 ms; 100K -> 2300 - 2340 ms
         //DbReaderWriterLock sp = new DbReaderWriterLock();           //1K -> 18-22ms; 10K -> 220-270 ms: 100K -> 2360-2430 ms
        private void TEST_SPIN_IncrementValue(int totalQ)
        {
            
            sp.EnterWriteLock();
            try
            {
                valToIncr++;
            }
            finally
            {
                sp.ExitWriteLock();
            }
            var cntr = DBreeze.Diagnostic.SpeedStatistic.GetCounter("i");
            Interlocked.Add(ref sharedCnt, 1);

            if (sharedCnt == totalQ)
            {
                cntr.Stop();
                Console.WriteLine("done");
                TEST_SPIN_PRINT();
            }
            //DBreeze.Diagnostic.SpeedStatistic.StopCounter("i");
        }

        public void TEST_SPIN_PRINT()
        {
            Console.WriteLine(valToIncr);
            DBreeze.Diagnostic.SpeedStatistic.PrintOut("i");
            DBreeze.Diagnostic.SpeedStatistic.ClearAll();
        }

        #endregion


        #region "TEST FOREACH INSERT"

        private void TEST_TABLE_FOREACH_INSERT()
        {
            engine.Scheme.DeleteTable("t1");

            DBreeze.Diagnostic.SpeedStatistic.StartCounter("I");    //10 s
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    //tran.SynchronizeTables("t$");

                    for (int i = 1; i < 1000000; i++)
                    {
                       // tran.Insert<int, string>("t1", i, "string"+i);
                        tran.Insert<int, DbAscii>("t1", i, "string" + i);
                    }

                    tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }
            DBreeze.Diagnostic.SpeedStatistic.PrintOut("I",true);


            DBreeze.Diagnostic.SpeedStatistic.StartCounter("U");    //25s
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    tran.SynchronizeTables("t$");

                    foreach (var row in tran.SelectForward<int, DbAscii>("t1"))
                    {
                        //tran.Insert<int, string>("t1", row.Key, "new string" + row.Key.ToString());
                        tran.Insert<int, DbAscii>("t1", row.Key, "new string" + row.Key.ToString());
                    }

                    tran.Commit();

                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }
            DBreeze.Diagnostic.SpeedStatistic.PrintOut("U", true);

            DBreeze.Diagnostic.SpeedStatistic.StartCounter("F");        // 6s
            string ss="";
            using (var tran = engine.GetTransaction())
            {
                try
                {

                    foreach (var row in tran.SelectForward<int, DbAscii>("t1"))
                    {
                        ss = row.Value.Get;
                        //Console.WriteLine("T1 K: {0}; V: {1}", row.Key, row.Value);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }
            DBreeze.Diagnostic.SpeedStatistic.PrintOut("F", true);

        }

        #endregion

        #region "TEST TABLE RESERVED FOR WRITE"

        private void TEST_TABLE_RESERVED_FOR_WRITE_1()
        {
            engine.Scheme.DeleteTable("t1");

            //using (var tran = engine.GetTransaction())
            //{
            //    try
            //    {
            //        //tran.SynchronizeTables("t$");

            //        tran.Insert<int, string>("t1", 12, "string12");

            //        tran.Commit();
            //    }
            //    catch (Exception ex)
            //    {
            //        Console.WriteLine("T1 YAHOO! " + ex.ToString());
            //    }
            //}


            using (var tran = engine.GetTransaction())
            {
                try
                {
                    var row = tran.Select<byte[], string>("t1", new byte[] {12,15,17});

                    if (row.Exists)
                    {
                        Console.WriteLine("T1 K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
                    }
                    else
                    {
                        Console.WriteLine("not");
                    }

                    tran.SynchronizeTables("t$");

                    row = tran.Select<byte[], string>("t1", new byte[] { 12, 15, 17 });

                    if (row.Exists)
                    {
                        Console.WriteLine("T1 K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
                    }
                    else
                    {
                        Console.WriteLine("not");
                    }


                    tran.Insert<byte[], string>("t1", new byte[] { 12, 15, 17 }, "string20");

                    
                    //tran.Commit();


                    row = tran.Select<byte[], string>("t1", new byte[] { 12, 15, 17 });

                    if (row.Exists)
                    {
                        Console.WriteLine("T1 K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
                    }
                    else
                    {
                        Console.WriteLine("not");
                    }

                    tran.Insert<byte[], string>("t1", new byte[] { 11, 15, 17 }, "string19");

                    row = tran.Select<byte[], string>("t1", new byte[] { 11, 15, 17 });

                    if (row.Exists)
                    {
                        Console.WriteLine("T1 K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
                    }
                    else
                    {
                        Console.WriteLine("not");
                    }



                    tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }
        }

        private void TEST_TABLE_RESERVED_FOR_WRITE()
        {
            engine.Scheme.DeleteTable("t1");

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    //tran.SynchronizeTables("t$");

                    tran.Insert<int, string>("t1", 12, "string12");

                    tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }


            using (var tran = engine.GetTransaction())
            {
                try
                {
                    var row = tran.Select<int, string>("t1", 12);

                    if (row.Exists)
                    {
                        Console.WriteLine("T1 K: {0}; V: {1}", row.Key, row.Value);
                    }
                    else
                    {
                        Console.WriteLine("not");
                    }

                    tran.SynchronizeTables("t$");

                    row = tran.Select<int, string>("t1", 12);

                    if (row.Exists)
                    {
                        Console.WriteLine("T1 K: {0}; V: {1}", row.Key, row.Value);
                    }
                    else
                    {
                        Console.WriteLine("not");
                    }
                                      

                    tran.Insert<int, string>("t1", 12, "string20");
                    

                    //tran.Commit();
                    

                    row = tran.Select<int, string>("t1", 12);

                    if (row.Exists)
                    {
                        Console.WriteLine("T1 K: {0}; V: {1}", row.Key, row.Value);
                    }
                    else
                    {
                        Console.WriteLine("not");
                    }

                    tran.Insert<int, string>("t1", 12, "string19");

                    row = tran.Select<int, string>("t1", 12);

                    if (row.Exists)
                    {
                        Console.WriteLine("T1 K: {0}; V: {1}", row.Key, row.Value);
                    }
                    else
                    {
                        Console.WriteLine("not");
                    }

                 

                    tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }
        }

        #endregion

        #region "TEST PARALLEL ACCESS AND CLOSING TABLES"

        private void TEST_START_PARALLEL_ACCESS_CLOSE()
        {
            Action a1 = () =>
                {
                    START_PARALLEL_ACCESS_T1();
                };

            a1.DoAsync();

            Action a2 = () =>
            {
                START_PARALLEL_ACCESS_T2();
            };

            a2.DoAsync();

            Action a3 = () =>
            {
                START_PARALLEL_ACCESS_T3();
            };

            a3.DoAsync();
        }

        private void START_PARALLEL_ACCESS_T1()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    tran.SynchronizeTables("t$");

                    tran.Insert<int, string>("t1", 12, "string12");
                    //Thread.Sleep(1000);

                    tran.Commit();


                    tran.Insert<int, string>("t2", 14, "string14");

                    var row = tran.Select<int, string>("t1", 12);

                    if (row.Exists)
                    {
                        Console.WriteLine("T1 K: {0}; V: {1}", row.Key, row.Value);
                    }
                    else
                    {
                        Console.WriteLine("T1 D E");
                    }

                    tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 YAHOO! " + ex.ToString());
                }
            }
        }

        private void START_PARALLEL_ACCESS_T2()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    var row1 = tran.Select<int, string>("t1", 12);

                    if (row1.Exists)
                    {
                        Console.WriteLine("T2 K: {0}; V: {1}", row1.Key, row1.Value);
                    }
                    else
                    {
                        Console.WriteLine("T2 D E");
                    }


                    tran.SynchronizeTables("t$");
                    

                    tran.Insert<int, string>("t1", 15, "string15");

                    //Thread.Sleep(1000);

                    tran.Commit();


                    tran.Insert<int, string>("t2", 17, "string17");

                    var row = tran.Select<int, string>("t1", 15);

                    if (row.Exists)
                    {
                        Console.WriteLine("T2 K: {0}; V: {1}", row.Key, row.Value);
                    }
                    else
                    {
                        Console.WriteLine("T2 D E");
                    }

                    tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T2 YAHOO! " + ex.ToString());
                }
            }
        }

        private void START_PARALLEL_ACCESS_T3()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    tran.SynchronizeTables("t$");


                    tran.Insert<int, string>("t1", 19, "string19");

                    //Thread.Sleep(1000);

                    tran.Commit();


                    tran.Insert<int, string>("t2", 27, "string27");

                    var row = tran.Select<int, string>("t1", 19);

                    if (row.Exists)
                    {
                        Console.WriteLine("T3 K: {0}; V: {1}", row.Key, row.Value);
                    }
                    else
                    {
                        Console.WriteLine("T3 D E");
                    }

                    tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T3 YAHOO! " + ex.ToString());
                }
            }
        }


        #endregion

        #region "TEST UPDATE GROWING"

        private void TEST_UPDATE_GROWS_UP()
        {

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        tran.Insert<int, string>("t1", i, "string");
                    }

                    tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("YAHOO! " + ex.ToString());
                }
            }

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        tran.Insert<int, string>("t1", i, "string123");
                    }

                    tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("YAHOO! " + ex.ToString());
                }
            }


            using (var tran = engine.GetTransaction())
            {
                try
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        tran.Insert<int, string>("t1", i, "string7898974654");
                    }

                    //Rolling back
                    //tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("YAHOO! " + ex.ToString());
                }
            }

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    foreach (var row in tran.SelectForward<int, string>("t1"))
                    {
                        Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                    }

                 
                }
                catch (Exception ex)
                {
                    Console.WriteLine("YAHOO! " + ex.ToString());
                }
            }

        }

        #endregion

        #region "TEST ROLLBACK and TRAN MANY TABLES"

        private void TEST_Nw_RB()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    tran.Insert<byte[], int>("t1", new byte[] { 0x12, 0x14, 0x17 }, 11);
                    tran.Insert<byte[], int>("t2", new byte[] { 0x12, 0x14, 0x17 }, 11);


                    // tran.Insert<byte[], int>("t1", new byte[] { 0x12, 0x14, 0x17 }, 9);
                    // tran.Insert<byte[], int>("t1", new byte[] { 0x12, 0x14, 0x18 }, 9);

                    //tran.Insert<byte[], int>("t1", new byte[] { 0x12, 0x14, 0x17 }, 2);

                    // tran.Insert<byte[], int>("t1", new byte[] { 0x12 }, 9);

                    tran.Rollback();
                    //tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("YAHOO! " + ex.ToString());
                }
            }

            //Read
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    Console.WriteLine("t1");
                    foreach (var row in tran.SelectForward<byte[], int>("t1"))
                    {
                        Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
                    }

                    Console.WriteLine("t2");
                    foreach (var row in tran.SelectForward<byte[], int>("t2"))
                    {
                        Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
                    }

                    //var row = tran.Select<byte[], int>("t1", new byte[] { 0x12, 0x14, 0x17 });

                    //if (row.Exists)
                    //{
                    //    Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);
                    //}
                    //else
                    //{
                    //    Console.WriteLine("DOES NOT EXIST");
                    //}

                    //tran.Commit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("YAHOO! " + ex.ToString());
                }
            }
        }
        #endregion

        #region "Different Writes"

        private void TEST_Write1MLN_DateTimeGrowing(string tableName, ulong quantity)
        {
            InitDb();

            Console.WriteLine("************  INSERT IS STARTED {0}; Thread: {1}", tableName, System.Threading.Thread.CurrentThread.ManagedThreadId);
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            using (var tran = engine.GetTransaction())
            {
                try
                {


                    DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0);

                    for (ulong i = 0; i < quantity; i++)
                    {

                        tran.Insert<DateTime, byte[]>(tableName, dt, null);
                        dt = dt.AddMinutes(7);
                    }


                    tran.Commit();


                }
                catch (System.Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }

            sw.Stop();
            Console.WriteLine("************  INSERT IS DONE {0}; Cnt: {1}; {2} ms; Thread: {3};", tableName, quantity, sw.ElapsedMilliseconds.ToString(), System.Threading.Thread.CurrentThread.ManagedThreadId);
        }

        #endregion


        #region "TestAsync 1"

        private void TA1()
        {
            Insert1MLN_TEST1_RA("t1");
            Insert1MLN_TEST1_RA("t2");
            Insert1MLN_TEST1_RA("t3");
            Insert1MLN_TEST1_RA("t4");
            Insert1MLN_TEST1_RA("t5");
            Insert1MLN_TEST1_RA("t6");
        }

        private void TA1_Thread1()
        {
            using (var tran = engine.GetTransaction())
            {              
                try
                {
                    Console.WriteLine("T1 started");
                    tran.Insert("t1", GB("aaa"), GB("oooo"));

                    System.Threading.Thread.Sleep(7000);
                    Console.WriteLine("T1 released");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
              
            }
        }

        private void TA2_Thread1()
        {
            using (var tran = engine.GetTransaction())
            {
                Console.WriteLine("T2 started");
                tran.Insert("t1", GB("aaa"), GB("oooo"));

                //System.Threading.Thread.Sleep(3000);
                Console.WriteLine("T2 released");
            }
        }

       


        #endregion

        #region "TEST Async Run TEST1 1"
        private void Insert1MLN_TEST1_RA(string tableName)
        {
            Action a = () =>
            {
                Insert1MLN_TEST1(tableName);
            };

            a.DoAsync();
        }

        private void Insert1MLN_TEST1(string tableName)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            using (var tran = engine.GetTransaction())
            {

                DateTime dt = new DateTime(1970, 1, 1);
                //dt = new DateTime(2200, 1, 1);
                byte[] testKey = null;
                for (int i = 0; i < 1000000; i++)
                {
                    testKey = dt.Ticks.To_8_bytes_array_BigEndian();
                    //LTrie.Add(testKey, new byte[] { 2 });
                    tran.Insert(tableName, testKey, new byte[] { 3 });
                    dt = dt.AddHours(1);
                    //dt = dt.AddSeconds(7);

                    //LTrie.Commit();
                }
                //Console.WriteLine(dt.ToString());
                tran.Commit();
            }
            sw.Stop();
            Console.WriteLine("TRAN INSERT IS DONE {0}; Elapsed {1} ms", tableName, sw.ElapsedMilliseconds);
        }
        #endregion

        #region "inserting and reading datetime"
        private void TEST_INSERTING_READING_DATETIME()
        {
            InitDb();


            using (var tran = engine.GetTransaction())
            {
                try
                {
                    DBreeze.Diagnostic.SpeedStatistic.StartCounter("INSERT DATETIME TEST1");                   

                    DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0);
                   
                    for (ulong i = 0; i < 1000000; i++)
                    {
                        
                        tran.Insert<DateTime, byte[]>("t1", dt, null);                     
                        dt = dt.AddSeconds(7);
                    }


                    tran.Commit();

                    DBreeze.Diagnostic.SpeedStatistic.StopCounter("INSERT DATETIME TEST1");

                    DBreeze.Diagnostic.SpeedStatistic.StartCounter("SELECT DATETIME TEST1");          

                    int p = 0;

                    foreach (var row in tran.SelectForward<DateTime, byte[]>("t1"))
                    {                    
                        //Console.WriteLine("K: {0}; V: {1}", row.Key.ToString("dd.MM.yyyy HH:mm:ss"), ((row.Value == null) ? "NULL" : "OK"));
                        p++;
                    }

                    DBreeze.Diagnostic.SpeedStatistic.StopCounter("SELECT DATETIME TEST1");          

                }
                catch (Exception ex)
                {
                    Console.WriteLine("YAHOO! " + ex.ToString());
                }

                DBreeze.Diagnostic.SpeedStatistic.PrintOut(true);
            }
        }
        #endregion

        #region "inserting and reading strings and objects"
        //TEst Result
        /*
         * DbXML
            INSERT IS DONE 107093  (List<string> with 3 values "kuku")
            FETCH IS DONE 115501 ms; Quantity: 1000000;
            FETCH 6000 ms without value acquiring
            FS=261 MB


            DbMJSON
            INSERT IS DONE 14130
            FETCH IS DONE 31423 ms; Quantity: 1000000;
            FETCH 6000 ms without value acquiring
            FS=41 MB

            DbUTF8
            INSERT IS DONE 10534   (27 UTF8 symbols => 27 bytes in our case)
            FETCH IS DONE 12908 ms; Quantity: 1000000;
            FETCH 6000 ms without value acquiring
         *  FS = 44MB
         */

        public class Akula
        {
            public Akula()
            {
                Viagr = 1;
                xP = new DateTime(1980, 1, 1);
            }
            public int Viagr { get; set; }
            public DateTime xP { get; set; }
            public string Str { get; set; }
        }

        public class MyFT
        {
            public MyFT()
            {
                xP = new DateTime(1975, 1, 1);
                Dict = new Dictionary<string, Akula>();
            }

            public DateTime xD { get; set; }

            public DateTime xP { get; set; }

            public Dictionary<string,Akula> Dict { get; set; }
        }

        private void TEST_INSERTING_READING_STRING()
        {
            InitDb();

           

            List<string> aa = new List<string>();
            aa.Add("kuku");
            aa.Add("kuku1");
            aa.Add("kuku2");

            string testInsertString = "kuku     kuku     kuku     ";

            Dictionary<string, List<string>> dTest = new Dictionary<string, List<string>>();
            dTest.Add("dsfs", new List<string> { "dfs", "dfdsf" });
            dTest.Add("dsfs$S/§Dfsdf", new List<string> { "dfssdf", "sdfdfdsf" });

            MyFT mft=new MyFT();
            mft.Dict.Add("dfsd", new Akula { xP = new DateTime(1990, 11, 11), Viagr = 2342, Str = "Привк" });
            mft.Dict.Add("dfsd2", new Akula { xP = new DateTime(1997, 11, 11), Viagr = 2342 });
            mft.Dict.Add("dfsd4", new Akula { xP = new DateTime(979, 11, 11), Viagr = 2342 });

            //CustomSerializator.Serializator = JsonConvert.SerializeObject;
            //CustomSerializator.Deserializator = JsonConvert.DeserializeObject;


            //string ppp = CustomSerializator.SerializeCustom(aa);
            //aa = JsonConvert.DeserializeObject<List<string>>(ppp);
            //aa = JsonConvert.DeserializeObject(ppp, typeof(List<string>));
            //string ppp = JsonConvert.SerializeObject(aa);
            //aa = JsonConvert.DeserializeObject<List<string>>(ppp);
           //// aa = ppp.DeserializeCustom<List<string>>();

            //return;
            //mft = JsonConvert.DeserializeObject<MyFT>(ppp);
            //return;

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();

                    //tran.Insert<uint, DbMJSON<Dictionary<string, List<string>>> >("t1", 1, dTest);
                    //tran.Insert<uint, DbMJSON<MyFT>>("t1", 1, mft);
                    //tran.Insert<uint, DbMJSON<MyFT>>("t1", 2, mft);

                    for (uint i = 0; i < 1000000; i++)
                    {
                        //tran.Insert<uint, DbCustomSerializer<List<string>>>("t1", i, aa);
                        //tran.Insert<uint, DbCustomSerializer<MyFT>>("t1", i, mft);
                        //tran.Insert<uint, DbMJSON<MyFT>>("t1", i, mft);
                        //tran.Insert<uint, DbXML<MyFT>>("t1", i, mft);

                        //tran.Insert<uint, DbMJSON<List<string>>>("t1", i, aa);
                        //tran.Insert<uint, DbXML<List<string>>>("t1", i, aa);

                        //tran.Insert<uint, UNICODEstring>("t1", i, "1");
                        //tran.Insert<uint, DbXML<List<string>>>("t1", i, new DbXML<List<string>>(aa));                        
                        //tran.Insert<uint, DbXML<List<string>>>("t1", i, null);
                        //tran.Insert<uint, ASCIIstring>("t1", i, new ASCIIstring("1"));
                        //tran.Insert<uint, ASCIIstring>("t1", i, new ASCIIstring("Hello You " + i));
                        //tran.Insert<uint, ASCIIstring>("t1", i, new ASCIIstring(String.Empty));
                        //tran.Insert<uint, string>("t1", i, null);
                        //tran.Insert<uint, string>("t1", i, String.Empty);
                        //tran.Insert<uint, string>("t1", i, "Hello You " + i);
                        // tran.Insert<uint, string>("t1", i, null);
                    }


                    tran.Commit();

                    sw.Stop();
                    Console.WriteLine("INSERT IS DONE " + sw.ElapsedMilliseconds.ToString());



                    sw.Reset();
                    sw.Start();

                    int p = 0;

                    foreach (var row in tran.SelectForward<uint, DbXML<MyFT>>("t1"))
                    //foreach (var row in tran.SelectForward<uint, DbMJSON<MyFT>>("t1"))
                    //foreach (var row in tran.SelectForward<uint, DbCustomSerializer<MyFT>>("t1"))
                    //foreach (var row in tran.SelectForward<uint, DbCustomSerializer<List<string>>>("t1"))
                    //foreach (var row in tran.SelectForward<uint, DbMJSON<List<string>>>("t1"))
                    //foreach (var row in tran.SelectForward<uint, DbXML<List<string>>>("t1"))
                    //foreach (var row in tran.SelectForward<uint, byte?>("t1"))
                    //foreach (var row in tran.SelectForward<uint,  DbMJSON<Dictionary<string, List<string>>> >("t1"))
                    //foreach (var row in tran.SelectForward<uint, DbMJSON<MyFT>>("t1"))
                    {
                        //aa = row.Value.Get;
                        //testInsertString = row.Value.ToString();
                        //dTest = row.Value.Get;
                        mft = row.Value.Get;
                        //Console.WriteLine("K: {0}; V: {1}", row.Key.ToString(), (row.Value == null) ? "NULL" : row.Value.Get.Count().ToString());
                        //Console.WriteLine("K: {0}; V: {1}", row.Key.ToString(), (row.Value == null) ? "NULL" : row.Value.Text);
                        //Console.WriteLine("K: {0}; V: {1}", row.Key.ToString(), (row.Value == null) ? "NULL" : row.Value);
                        p++;
                    }

                    sw.Stop();
                    Console.WriteLine("FETCH IS DONE {0} ms; Quantity: {1};", sw.ElapsedMilliseconds.ToString(), p);

                }
                catch (Exception ex)
                {
                    Console.WriteLine("YAHOO! " + ex.ToString());
                }

            }
        }
        #endregion

        #region "TEST 15 parallel read and removing (with file recreation) data from one table"


        public void TEST15_ReadParallel()
        {
            InitDb();

            Console.WriteLine("************  Fetch IS STARTED {0}; Thread: {1}", "t1", System.Threading.Thread.CurrentThread.ManagedThreadId);

            using (var tran = engine.GetTransaction())
            {
                try
                {


                    DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0);

                    foreach (var row in tran.SelectForward<DateTime, byte[]>("t1"))
                    {
                        if (row.Exists)
                        {
                            Console.WriteLine("K: {0}; V: {1}", row.Key.ToString("dd.MM.yyyy HH:mm:ss"), ((row.Value == null) ? "NULL" : "OK"));
                        }
                        else
                        {
                            Console.WriteLine("YAHOO!");
                        }
                    }


                }
                catch (System.Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }

            Console.WriteLine("************  Fetch IS Finished {0}; Thread: {1}", "t1", System.Threading.Thread.CurrentThread.ManagedThreadId);
        }

        public void TEST15_RemoveAllWithFileRecreation()
        {
            InitDb();

            //using (var tran = engine.GetTransaction())
            //{
            //    try
            //    {
            //        //Result: Reading thread will throw exception, seems to be not bad behaviour.
            //        //tran.RemoveAll("t1", true);

            //        //Result:
            //        tran.RemoveAll("t1", false);
            //        //System.Threading.Thread.Sleep(10000);
            //        tran.Commit();

            //    }
            //    catch (System.Exception ex)
            //    {
            //        Console.WriteLine(ex.ToString());
            //    }
            //}

            engine.Scheme.DeleteTable("t1");
        }

        public void TEST15_Write()
        {
            InitDb();

            Console.WriteLine("************  INSERT IS STARTED {0}; Thread: {1}", "t1", System.Threading.Thread.CurrentThread.ManagedThreadId);
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            using (var tran = engine.GetTransaction())
            {
                try
                {


                    DateTime dt = new DateTime(1980, 1, 1, 0, 0, 0);

                    for (ulong i = 0; i < 1000; i++)
                    {

                        tran.Insert<DateTime, byte[]>("t1", dt, null);
                        dt = dt.AddMinutes(7);
                    }


                    tran.Commit();


                }
                catch (System.Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }

            sw.Stop();
            Console.WriteLine("************  INSERT IS DONE {0}; Cnt: {1}; {2} ms; Thread: {3};", "t1", 1000, sw.ElapsedMilliseconds.ToString(), System.Threading.Thread.CurrentThread.ManagedThreadId);
        }
        #endregion

        #region "Test Schema TEST 1"

        private void TESTSCHEMA_TEST1()
        {
            InitDb();

            //inserting 10 different tables

            //using (var tran = engine.GetTransaction())
            //{
            //    tran.Insert<uint, uint>("t1", 1, 1);
            //    tran.Insert<uint, uint>("t2", 1, 1);
            //    tran.Insert<uint, uint>("t11", 1, 1);
            //    tran.Insert<uint, uint>("abc", 1, 1);

            //    tran.Commit();
            //}


            //Console.WriteLine(engine.Schema.GetPhysicalPathToTheUserTable("t2456"));

            List<string> l = engine.Scheme.GetUserTableNamesStartingWith("t2");

            foreach (var ss in l)
            {
                Console.WriteLine("UserTable: {0}", ss);
            }

        }


        private void TESTSCHEMA_TEST_DELETETABLE()
        {
            InitDb();
            //OK
            engine.Scheme.DeleteTable("t1");
        }

        #endregion

        #region "TEST READ_SYNCHRO"

        private void TESTREADSYNCHRO_TEST()
        {
            InitDb();

            using (var tran = engine.GetTransaction())
            {
                tran.Insert<uint, uint>("t1", 1, 1);
                tran.Insert<uint, uint>("t1", 3, 1);
                //tran.Commit();

                foreach (var row in tran.SelectForward<uint, uint>("t1"))
                {
                    if (row.Exists)
                    {
                        Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                    }
                    else
                    {
                        Console.WriteLine("doesn't exist!");
                    }
                }

                tran.Rollback();

                tran.Insert<uint, uint>("t1", 2, 1);
                //tran.Commit();

                foreach (var row in tran.SelectForward<uint, uint>("t1"))
                {
                    if (row.Exists)
                    {
                        Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                    }
                    else
                    {
                        Console.WriteLine("doesn't exist!");
                    }
                }


                //tran.Commit();
            }
        }

        public void TESTREADSYNCHRO_Parallel()
        {
            InitDb();

            using (var tran = engine.GetTransaction())
            {
                foreach (var row in tran.SelectForward<uint, uint>("t1"))
                {
                    if (row.Exists)
                    {
                        Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                    }
                    else
                    {
                        Console.WriteLine("doesn't exist!");
                    }
                }             
            }
        }

        #endregion

        #region "TEST Tables synchronization"

        private void TEST_TABLES_SYNCHRONIZATION()
        {
            InitDb();

            using (var tran = engine.GetTransaction())
            {
                tran.SynchronizeTables("Auto#/Items");

                tran.Insert<int, int>("Auto132/Items", 1, 1);

                tran.Commit();
            }
        }

        #endregion

        #region "TST EMULATE DEADLOCK"

        private void TEST_START_DEADLOCKCASE()
        {
            Action a = () =>
            {
                TEST_START_DEADLOCKCASE_T1();
            };

            a.DoAsync();

            Action a1 = () =>
            {
                TEST_START_DEADLOCKCASE_T2();
            };

            a1.DoAsync();
        }

        private void TEST_START_DEADLOCKCASE_T1()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    tran.SynchronizeTables("t*");

                    Console.WriteLine("T1 started");

                    tran.Insert<int, int>("t2", 1, 1);
                    Thread.Sleep(500);
                    tran.Insert<int, int>("t1", 1, 1);

                    Thread.Sleep(1000);
                    tran.Commit();
                    Console.WriteLine("T1 finished");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T1 ex: " + ex.ToString());
                }
               
            }
        }

        private void TEST_START_DEADLOCKCASE_T2()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    tran.SynchronizeTables("t*");
                    Console.WriteLine("T2 started");

                    tran.Insert<int, int>("t1", 1, 1);
                    Thread.Sleep(500);
                    tran.Insert<int, int>("t2", 1, 1);

                    Thread.Sleep(1000);
                    tran.Commit();
                    Console.WriteLine("T2 finished");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("T2 ex: " + ex.ToString());
                }
                
            }
        }
        #endregion

        #region "TEST SkipFrom"

        private void PrintKey(int k)
        {
            Console.WriteLine("Key: {0}; Bt: {1}", k, k.To_4_bytes_array_BigEndian().ToBytesString(""));
        }

        private void TEST_SKIP_FROM()
        {
            using (var tran = engine.GetTransaction())
            {
                
                //tran.Insert<byte[], int>("t1", new byte[] { 0x80, 0x80, 0x12, 0x00 }, 1);
                //tran.Insert<byte[], int>("t1", new byte[] { 0x80, 0x7E, 0xFF, 0x00 }, 2);

                //tran.Insert<byte[], int>("t1", new byte[] { 0x7E, 0xFF, 0xFF, 0x00 }, 3);

                //tran.Insert<int, int>("t1", Int32.MinValue, 1);
                //tran.Insert<int, int>("t1", Int32.MaxValue, 1);
                //tran.Insert<int, int>("t1", -1048593, 1);
                //tran.Insert<int, int>("t1", -1048592, 1);
                //tran.Insert<int, int>("t1", -1048590, 1);
                //tran.Insert<int, int>("t1", -300, 1);
                //tran.Insert<int, int>("t1", -85, 1);
                //tran.Insert<int, int>("t1", -15, 1);
                //tran.Insert<int, int>("t1", 0, 1);
                //tran.Insert<int, int>("t1", 1, 1);
                //tran.Insert<int, int>("t1", 2, 1);
                //tran.Insert<int, int>("t1", 3, 1);
                //tran.Insert<int, int>("t1", 4, 1);
                //tran.Insert<int, int>("t1", 5, 1);
                //tran.Insert<int, int>("t1", 50, 1);
                //tran.Insert<int, int>("t1", 90, 1);

                //tran.Commit();

                //int k = (new byte[] { 0x7F, 0xEF, 0xFF, 0xEF }).To_Int32_BigEndian();
                //PrintKey(Int32.MinValue);
                //PrintKey(-1048593);
                //PrintKey(-1048592);
                //PrintKey(-1048590);
                //PrintKey(-17);
                //PrintKey(Int32.MaxValue);
                //PrintKey(0);
                //PrintKey(1);
                //PrintKey(2);
                //PrintKey(3);
                //PrintKey(4);
                //PrintKey(5);


                //foreach (var row in tran.SelectForward<int, int>("t1"))
                //foreach (var row in tran.SelectForwardSkipFrom<int, int>("t1",-17,0))
                //foreach (var row in tran.SelectBackwardSkipFrom<byte[], int>("t1", new byte[] { 0x80, 0x7F, 0x14, 0x00 }, 0))
                //foreach (var row in tran.SelectBackwardStartFrom<int, int>("t1", 3, true))
                //foreach (var row in tran.SelectForwardSkipFrom<int, int>("t1", -1048595, 0))
                //foreach (var row in tran.SelectForwardSkipFrom<int, int>("t1", -15, 0))
                //foreach (var row in tran.SelectForwardSkipFrom<int, int>("t1", -1, 1))
               // foreach (var row in tran.SelectForwardStartFrom<int, int>("t1", -1048597, true))

                foreach (var row in tran.SelectBackwardFromTo<int, int>("t1", 4, false, 5, false))
                //foreach (var row in tran.SelectForwardFromTo<int, int>("t1", 3, true, 60, false))
                {
                    //Console.WriteLine("K: {0}; V: {1}", row.Key.ToBytesString(""), row.Value);

                    Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                }
            }
        }

        #endregion

        #region "TEST DbConversion Speed"

        private void Test_DbTypesConversion()
        {          

            engine.Scheme.DeleteTable("t1");

            using (var tran = engine.GetTransaction())
            {
                //System.Diagnostics.Stopwatch sw = new Stopwatch();
                //sw.Start();

                //DBreeze.Test.TestStatic.StartCounter("TOTAL RUN");

                DateTime dt = DateTime.Now;

                for (ulong i = 0; i < 1000000; i++)
                {                   
                    //tran.Insert<byte[], byte[]>("t1", i.To_8_bytes_array_BigEndian(), new byte[] { 1 }); //12000                 
                    tran.Insert<DateTime, byte[]>("t1", dt, new byte[] { 1 });  //16000
                    //tran.Insert<byte[], byte[]>("t1", dt.Ticks.To_8_bytes_array_BigEndian(), new byte[] { 1 });
                    dt = dt.AddSeconds(7);
                }
                tran.Commit();

                //var cnt = tran.Count("t1");
                DBreeze.Diagnostic.SpeedStatistic.StopCounter("TOTAL RUN");
                //sw.Stop();
                //Console.WriteLine("E: {0}", sw.ElapsedMilliseconds);
                
                //tran.Commit();

                DBreeze.Diagnostic.SpeedStatistic.PrintOut();
                DBreeze.Diagnostic.SpeedStatistic.ClearAll();
            }
        }

        #endregion

        #region "TEST Artikel"

        private class Artikel
        {
            public string Name { get; set; }
            public float Price { get; set; }
        }

        private void Test_Artikel()
        {

            //engine.Schema.DeleteTable("t1");

            DBreeze.Diagnostic.SpeedStatistic.StartCounter("GS");

            using (var tran = engine.GetTransaction())
            {
                DateTime dt=DateTime.Now;

                Artikel b = null;

                float pr = 14.25f;
                ulong pr1=UInt64.MaxValue;
                byte[] fb = pr.To_4_bytes_array_BigEndian().Concat(pr1.To_8_bytes_array_BigEndian());

                Console.WriteLine(fb.ToBytesString(""));

                foreach (var row in tran.SelectForward<byte[], ulong>("Prices"))
                {
                    Console.WriteLine("K:{0}; V: {1}", row.Key.ToBytesString(""), row.Value);
                }

                foreach (var row in tran.SelectForwardStartFrom<byte[], ulong>("Prices", fb, true))
                {
                    Console.WriteLine("V: {0}", row.Value);
                }


                //Artikel a = null;
                //ulong k = 1;

                //a = new Artikel()
                //{
                //     Name = "Notebook",
                //     Price = 12.12f
                //};
                              

                //tran.Insert<ulong, DbMJSON<Artikel>>("Artikel", k, a);                
                //tran.Insert<byte[], ulong>("Prices", a.Price.To_4_bytes_array_BigEndian().Concat(k.To_8_bytes_array_BigEndian()),k);


                //a = new Artikel()
                //{
                //    Name = "Mouse",
                //    Price = 14.25f
                //};

                //k = tran.Count("Artikel")+1;
                //tran.Insert<ulong, DbMJSON<Artikel>>("Artikel", k, a);
                //tran.Insert<byte[], ulong>("Prices", a.Price.To_4_bytes_array_BigEndian().Concat(k.To_8_bytes_array_BigEndian()), k);

                //a = new Artikel()
                //{
                //    Name = "HDD",
                //    Price = 140.25f
                //};

                //k = tran.Count("Artikel") + 1;
                //tran.Insert<ulong, DbMJSON<Artikel>>("Artikel", k, a);
                //tran.Insert<byte[], ulong>("Prices", a.Price.To_4_bytes_array_BigEndian().Concat(k.To_8_bytes_array_BigEndian()), k);

                //a = new Artikel()
                //{
                //    Name = "Keyboard",
                //    Price = 12.12f
                //};

                //k = tran.Count("Artikel") + 1;
                //tran.Insert<ulong, DbMJSON<Artikel>>("Artikel", k, a);
                //tran.Insert<byte[], ulong>("Prices", a.Price.To_4_bytes_array_BigEndian().Concat(k.To_8_bytes_array_BigEndian()), k);


                //tran.Commit();

                //foreach (var row in tran.SelectForwardStartsWith<string, string>("t1", "ww"))
                //foreach (var row in tran.SelectForward<string, string>("t1"))
                //{
                //    Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                //}



                DBreeze.Diagnostic.SpeedStatistic.StopCounter("GS");

                DBreeze.Diagnostic.SpeedStatistic.PrintOut();
                //for (ulong i = 0; i < 1; i++)
                //{
                //    tran.Insert<string, string>("t1", "1", "");
                //}
                //tran.Commit();


                //foreach (var row in tran.SelectForward<string, string>("t1"))
                //{
                //    Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                //}

            }
        }

        #endregion

        #region "TEST strings"

        private void Test_Strings()
        {          

            engine.Scheme.DeleteTable("t1");

            using (var tran = engine.GetTransaction())
            {

                for (ulong i = 0; i < 1; i++)
                {
                    tran.Insert<string, string>("t1", "1", null);
                }
                tran.Commit();

                foreach (var row in tran.SelectForward<string, string>("t1"))
                {
                    Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                }
            }
        }

        #endregion


        #region "TEST Partial Insert"

        private void Test_Partial_Insert()
        {

            //engine.Schema.DeleteTable("t1");

            using (var tran = engine.GetTransaction())
            {
                DBreeze.Diagnostic.SpeedStatistic.StartCounter("BOOL CONVERT");


                for (int i = 0; i < 1000000; i++)
                {                    
                    tran.Insert<int, bool?>("t1", i, true);
                }
                tran.Commit();
                DBreeze.Diagnostic.SpeedStatistic.StopCounter("BOOL CONVERT");

                DBreeze.Diagnostic.SpeedStatistic.PrintOut(true);
                //tran.Insert<string, byte[]>("t1", "1", new byte[]{1,2,3,4,5,6,7,8,9,10,11});
                //tran.Insert<string, byte[]>("t1", "1", new byte[]{1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18});
                //tran.Insert<string, byte[]>("t1", "1", new byte[] { 1, 2, 3 });
                //tran.Insert<string, byte[]>("t1", "1", new byte[0] );
                //tran.InsertPart<string, byte[]>("t1", "1", new byte[]{1},0);
                //tran.InsertPart<string, byte[]>("t1", "1", null, 60);

                //tran.Insert<int, bool?>("t1", 1, true);

                //tran.Commit();

                //for (ulong i = 0; i < 1; i++)
                //{
                //    tran.Insert<string, string>("t1", "1", null);
                //}
                //tran.Commit();

                //byte[] val =null;
                ////foreach (var row in tran.SelectForward<string, byte[]>("t1"))
                //foreach (var row in tran.SelectForward<int, bool?>("t1"))
                //{
                //    //Console.WriteLine("K: {0}; V: {1}", row.Key, row.Value);
                //    //val = row.Value;
                //}
            }
        }

        #endregion


        #region "TEST LTRIE Fetch and parallel delete (keys and values tets)"
        /*
         * TEST 1
           Filling data first, then starting Thread 1 (fetch), while its fetching (till value 100) running thread 2, who deletes value 100, checking result
         * Results:
         * Fetching thread has shown deleted value and key like they were in previous state
         * 
         * TEST 2
         * Filling data first, then starting Thread 1 (fetch), while its fetching (till value 100) running thread 2, who update value 100, checking result
         * Results:
         * Fetching thread has shown modified value
         */
        private void TEST_PAR_FETCH_DELETE()
        {
            //Filling data first, then starting Thread 1 (fetch), while its fetching running thread 2, who is deleting value 100, checking result

            InitLTrieAscii();

            Console.WriteLine("LTrie filling started");

            //Filling table with data
            for (uint i = 0; i < 200; i++)
            {
                LTrie.Add(i.To_4_bytes_array_BigEndian(), new byte[] { 1 });
            }

            LTrie.Commit();

            Console.WriteLine("LTrie filling finished");
        }

        private void TEST_PAR_FETCH_DELETE_fetch()
        {
            InitLTrieAscii();
            //Thread will read data and put it into consloe

            foreach (var row in LTrie.IterateForward())
            {
                //Console.WriteLine("READ KEY: {0}", row.Key.To_UInt32_BigEndian());

                Console.WriteLine("READ KEY: {0}; V: {1}", row.Key.To_UInt32_BigEndian(),row.GetFullValue(true).ToBytesString(""));
                Thread.Sleep(100);
            }
        }

        private void TEST_PAR_FETCH_DELETE_del()
        {
            InitLTrieAscii();
            //Thread will read data and put it into consloe

            uint key2remove = 100;
            byte[] btkey2remove = key2remove.To_4_bytes_array_BigEndian();
            
            //In test 1 we remove key. 
            //In test 2 we update key on value

            //Updating key on new value
            LTrie.Add(btkey2remove, new byte[] { 2 });
            //Removing
            //LTrie.Remove(ref btkey2remove);


            LTrie.Commit();

            Console.WriteLine("Key {0} was deleted", key2remove);
        }


        #endregion


        #region "TEST DBREEZE Fetch and parallel delete (keys and values tets)"
        /*
         * TEST 1
           Filling data first, then starting Thread 1 (fetch), while its fetching running thread 2, who deletes value new byte[] { 2, 2, 1 }
         * Results:
         * When fetching thread came to value { 2, 2, 1 }, foreach doesn't see this key and skips to { 2, 3, 1 }
         * Resume:
         * Reading thread didn't acquire table synchronization so result can be counted as satisfactory.  
         * 
         * TEST 2
         * Filling data first, then starting Thread 1 (fetch), while its fetching running thread 2, who value value new byte[] { 2, 2, 1 }
         * Results:
         * When fetching thread came to value { 2, 2, 1 } it has shown key { 2, 2, 1 } and modified value (byte[] {2} instead of 1)
         * Resume:
         * Reading thread didn't acquire table synchronization so result can be counted as satisfactory
         */
        private void TEST_DBR_PAR_FETCH_DELETE()
        {
            //Filling data first, then starting Thread 1 (fetch), while its fetching running thread 2, who is deleting value 100, checking result

            InitDb();

            Console.WriteLine("Dbreeze filling started");

            engine.Scheme.DeleteTable("t1");

            using (var tran = engine.GetTransaction())
            {
                tran.Insert<byte[], byte[]>("t1", new byte[] { 1, 1, 1 }, new byte[] { 1 });
                tran.Insert<byte[], byte[]>("t1", new byte[] { 1, 2, 1 }, new byte[] { 1 });
                tran.Insert<byte[], byte[]>("t1", new byte[] { 1, 3, 1 }, new byte[] { 1 });

                tran.Insert<byte[], byte[]>("t1", new byte[] { 2, 1, 1 }, new byte[] { 1 });
                tran.Insert<byte[], byte[]>("t1", new byte[] { 2, 2, 1 }, new byte[] { 1 });
                tran.Insert<byte[], byte[]>("t1", new byte[] { 2, 3, 1 }, new byte[] { 1 });

                ////Filling table with data
                //for (uint i = 0; i < 200; i++)
                //{
                //    tran.Insert<uint, byte[]>("t1", i, new byte[] { 1 });                    
                //}

                tran.Commit();
            }

            Console.WriteLine("Dbreeze filling finished");
        }

        private void TEST_DBR_PAR_FETCH_DELETE_fetch(int thrNumber)
        {
            InitDb();
            //Thread will read data and put it into consloe

            using (var tran = engine.GetTransaction())
            {
                //tran.SynchronizeTables("t$");
                try
                {
                    foreach (var row in tran.SelectForward<byte[], byte[]>("t1"))
                    {
                        Console.WriteLine("READ KEY: {0}", row.Key.ToBytesString(""));
                        //Console.WriteLine("SLEEPING");
                        Thread.Sleep(2000);
                        //Console.WriteLine("Getting key value");
                        Console.WriteLine("{2} - READ KEY: {0}; V: {1}", row.Key.ToBytesString(""), row.Value.ToBytesString(""), thrNumber);
                        //Thread.Sleep(2000);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
                
            }
        }

        private void TEST_DBR_PAR_FETCH_DELETE_del()
        {
            InitDb();
            //Thread will read data and put it into console
                       

            using (var tran = engine.GetTransaction())
            {
                //tran.SynchronizeTables("t1");

                byte[] key2remove = new byte[] { 2, 2, 1 };

                //tran.RemoveKey<byte[]>("t1", key2remove);
                //tran.Insert<byte[], byte[]>("t1", new byte[] { 2, 2, 1 }, new byte[] { 2 });

                tran.RemoveAllKeys("t1", false);
                tran.Commit();

                //tran.RemoveAllKeys("t1", true);
               

                Console.WriteLine("Key {0} was deleted", key2remove.ToBytesString(""));
            }           

            
        }


        ///*
        //* TEST 1
        //  Filling data first, then starting Thread 1 (fetch), while its fetching (till value 100) running thread 2, who deletes value 100, checking result
        //* Results:
        //* When fetching thread came to value 100, it has shown deleted key 100 and its value like they were in previous state.
        //* Resume:
        //* Reading thread didn't acquire table synchronization so result can be counted as satisfactory.
        //* In synchronized scenario all works like expected. Key was deleted by thread 2 only fetch of thread 1 was finished
        //* 
        //* TEST 2
        //* Filling data first, then starting Thread 1 (fetch), while its fetching (till value 100) running thread 2, who update value 100, checking result
        //* Results:
        //* When fetching thread came to value 100 it has shown key 100 and modified value (byte[] {2} instead of 1)
        //* Resume:
        //* Reading thread didn't acquire table synchronization so result can be counted as satisfactory
        //*/
        //private void TEST_DBR_PAR_FETCH_DELETE()
        //{
        //    //Filling data first, then starting Thread 1 (fetch), while its fetching running thread 2, who is deleting value 100, checking result

        //    InitDb();

        //    Console.WriteLine("Dbreeze filling started");

        //    using (var tran = engine.GetTransaction())
        //    {
        //        //Filling table with data
        //        for (uint i = 0; i < 200; i++)
        //        {
        //            tran.Insert<uint, byte[]>("t1", i, new byte[] { 1 });
        //        }

        //        tran.Commit();
        //    }

        //    Console.WriteLine("Dbreeze filling finished");
        //}

        //private void TEST_DBR_PAR_FETCH_DELETE_fetch(int thrNumber)
        //{
        //    InitDb();
        //    //Thread will read data and put it into consloe

        //    using (var tran = engine.GetTransaction())
        //    {
        //        tran.SynchronizeTables("t$");

        //        foreach (var row in tran.SelectForward<uint, byte[]>("t1"))
        //        {
        //            //Console.WriteLine("READ KEY: {0}", row.Key.To_UInt32_BigEndian());

        //            Console.WriteLine("{2} - READ KEY: {0}; V: {1}", row.Key, row.Value.ToBytesString(), thrNumber);
        //            Thread.Sleep(100);
        //        }
        //    }
        //}

        //private void TEST_DBR_PAR_FETCH_DELETE_del()
        //{
        //    InitDb();
        //    //Thread will read data and put it into console



        //    using (var tran = engine.GetTransaction())
        //    {
        //        uint key2remove = 100;

        //        //tran.RemoveKey<uint>("t1", key2remove);
        //        tran.Insert<uint, byte[]>("t1", key2remove, new byte[] { 2 });

        //        tran.Commit();
        //        Console.WriteLine("Key {0} was deleted", key2remove);
        //    }


        //}
        #endregion

      

        #endregion
    }
}
