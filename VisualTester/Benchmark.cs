using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using DBreeze;
using DBreeze.Utils;
using DBreeze.Utils.Async;
using DBreeze.DataTypes;
using DBreeze.Diagnostic;

namespace VisualTester
{
    public class Benchmark : IDisposable
    {
        static DBreezeEngine engine = null;
        string _folder = String.Empty;

        public Benchmark(string dbreezeFolder)
        {
            _folder = dbreezeFolder;
            if(engine == null)
                engine = new DBreezeEngine(dbreezeFolder);
        }

        public void Dispose()
        {
            if (engine != null)
                engine.Dispose();
        }

        public void Start()
        {
            HDD_warmUp();

            //CHECK FS

            TEST_IN_MEMORY();

            //TEST_10_1();
            //TEST_12();

            //TEST_11();

            //TEST_10();

            //TEST_9_2();
            //TEST_9_1();

            //TEST_9_3();
            //TEST_9();

            //TEST_8_9();
            //TEST_8_8();
            //TEST_8_7();
            //TEST_8_6();
            //TEST_8_5();
            //TEST_8();
            //TEST_7();
            //TEST_6();
            //TEST_5_STARTER();
            //TEST_4();
            //TEST_3_1_STARTER();
            //TEST_3_1_STARTER_2();
            //TEST_3_2();
            //TEST_3();
            //TEST_2();
            //TEST_1_9();
            //TEST_1();

            DBreeze.Diagnostic.SpeedStatistic.PrintOut();
        }

        private void HDD_warmUp()
        {
            
            using (var tran = engine.GetTransaction())
            {
                tran.Insert<int, byte[]>("HDDwarmUp", 1, null);
                tran.Commit();
            }

            //engine.Schema.DeleteTable("HDDwarmUp");
        }

        private void TEST_IN_MEMORY()
        {
            DBreeze.DBreezeEngine memeng = new DBreezeEngine(new DBreezeConfiguration()
                {
                     Storage =  DBreezeConfiguration.eStorage.MEMORY
                });

            memeng.Scheme.DeleteTable("t1");

            //using (var tran = memeng.GetTransaction())
            //{
            //    for (int i = 0; i < 256; i++)
            //    {
            //        tran.Insert<byte[], int>("t1", new byte[0], i);
            //        tran.Insert<byte[], int>("t1", new byte[] { (byte)i }, i);
            //    }

            //    tran.Commit();
            //    var cnnt = tran.Count("t1");
            //}

            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);

            //return;

            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            //Dictionary<int, int> _d = new Dictionary<int, int>();
            //for (int i = 0; i < 1000000; i++)
            //{
            //    _d.Add(i, i);
            //}
            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);

            DBreeze.Diagnostic.SpeedStatistic.StartCounter("a");
            SortedDictionary<int, int> _sd = new SortedDictionary<int, int>();
            for (int i = 0; i < 1000000; i++)
            {
                _sd.Add(i, i);
            }
            DBreeze.Diagnostic.SpeedStatistic.PrintOut("a", true);


            using (var tran = memeng.GetTransaction())
            {
                DBreeze.Diagnostic.SpeedStatistic.StartCounter("c");
                for (int i = 0; i < 1000000; i++)
                {
                    tran.Insert<int, int>("t1", i, i);
                }

               

                tran.Commit();

                //int cnttt1 = 0;
                //foreach (var row in tran.SelectForward<int, int>("t1"))
                //{
                //    cnttt1++;
                //}

                DBreeze.Diagnostic.SpeedStatistic.PrintOut("c", true);
                Console.WriteLine(tran.Count("t1"));
            }

            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("b", true);

        }

        #region "Parallel starter"

        private void StartInThread1()
        {
            Action a = () =>
                {
                    TEST_3_1("t31");
                };

            a.DoAsync();
        }

        private void StartInThread2()
        {
            Action a = () =>
            {
                TEST_3_1("t32");
            };

            a.DoAsync();
        }

        private void StartInThread3()
        {
            Action a = () =>
            {
                TEST_3_1("t33");
            };

            a.DoAsync();
        }

        private void StartInThread4()
        {
            Action a = () =>
            {
                TEST_3_1("t34");
            };

            a.DoAsync();
        }

        private void StartInThread5()
        {
            Action a = () =>
            {
                TEST_3_1("t35");
            };

            a.DoAsync();
        }

        #endregion

        private void TEST_12()
        {
            SpeedStatistic.StartCounter("START ALL");
            for (int i = 0; i < 1000000; i++)    //3 ms empty loop
            {
                //try
                //{  //3 ms with try-catch
                //}
                //catch (Exception ex)
                //{
                //    Console.WriteLine(ex.ToString());
                //}

                //using (var tran = engine.GetTransaction())        //5820 ms with and without try-catch
                //{
                    
                //}

                DBreeze.Diagnostic.SpeedStatistic.StartCounter("GET");
                //DbReaderWriterLock _sync_transactionWriteTables = new DbReaderWriterLock();
                System.Threading.ReaderWriterLockSlim rwls = new System.Threading.ReaderWriterLockSlim();
                //DBreeze.Transactions.TransactionUnit1 transactionUnit = new DBreeze.Transactions.TransactionUnit1(null);

                //var tran = engine.GetTransaction();
                DBreeze.Diagnostic.SpeedStatistic.StopCounter("GET");

                //DBreeze.Diagnostic.SpeedStatistic.StartCounter("UNREGISTER1");
                ////tran.Dispose();
                //DBreeze.Diagnostic.SpeedStatistic.StopCounter("UNREGISTER1");
            }
            //SpeedStatistic.PrintOut("START ALL",true);


            SpeedStatistic.PrintOut();
            SpeedStatistic.ClearAll();
        }

        #region "TEST 11 inserting neurons connections"

        //Every thread chooses one neuron in the range from 1 - 1MLN
        //and inserts from 1 up to 100 connections inside

        private void TEST_11()
        {
            engine.Scheme.DeleteTable("NeuronsConnections");

            //Neuro save
            this.threadsQuantity = 100;
            this.totalElapsed = 0;
            this.totalElapsedQ = 0;

            //quantity of neurons which must be inserted by one thread
            ulong quantityOfNeuronConnections = 1000;

            SpeedStatistic.StartCounter("START ALL");
            for (int i = 0; i < threadsQuantity; i++)
            {
                //TEST_10_insertNeuron((ulong)(i * insertByOneThread), (ulong)insertByOneThread, false);

                TEST_11_insertNeuronConnection(quantityOfNeuronConnections, false);
            }

        }

        Random rndNeuron = new Random();

        private void TEST_11_insertNeuronConnection(ulong quantityOfConnections, bool showSingleStat)
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {

                    ulong neuron = (ulong)rndNeuron.Next(1000000);

                    string statTable = "INSERT NEURON " + neuron.ToString() + tran.ManagedThreadId.ToString();

                    SpeedStatistic.StartCounter(statTable);

                    byte[] key = null;

                   
                    for (ulong i = 0; i < quantityOfConnections; i++)
                    {
                        ulong neuronConnection = (ulong)rndNeuron.Next(1000000);

                        key = neuron.To_8_bytes_array_BigEndian().Concat(neuronConnection.To_8_bytes_array_BigEndian());

                        tran.Insert<byte[], byte[]>("NeuronsConnections", key, null);
                        tran.Commit();
                    }


                    var cntr = SpeedStatistic.GetCounter(statTable);

                    cntr.Stop();

                    if (showSingleStat)
                        cntr.PrintOut();
                   

                    TEST_10_VisMid(cntr, "NeuronsConnections");



                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        #endregion

        #region "TEST 10 inserting neurons"

        int tc = 0;
        object lock_tc = new object();
        DateTime startTc = new DateTime();

        private void TEST_10_1()
        {
            startTc = DateTime.Now;

            for (int i = 1; i < 7; i++)
            {

                var tr = new System.Threading.ParameterizedThreadStart((a) =>
                {
                    Console.WriteLine(a);

                    using (var tran = engine.GetTransaction())
                    {
                        //SpeedStatistic.StartCounter("i " + a.ToString());

                        for (int j = -200000; j < 800000; j++)
                        {
                            tran.Insert<int, int>("t" + a.ToString(), j, j);
                        }

                        tran.Commit();

                        //SpeedStatistic.PrintOut("i " + a.ToString(), true);

                        lock (lock_tc)
                        {
                            tc++;
                            if (tc == 6)
                            {
                                Console.WriteLine("FIN: " + DateTime.Now.Subtract(startTc).TotalMilliseconds.ToString());
                            }
                        }

                    }

                });

                new System.Threading.Thread(tr).Start(i);
                
            }
        }

      


       

        /// <summary>
        /// Starter, can be change quantity of threads and insertByOneThread (quant of neurons which must insert every thread)
        /// all happens with one table
        /// </summary>
        private void TEST_10()
        {
            engine.Scheme.DeleteTable("Neurons");

            //Neuro save
            this.threadsQuantity = 100;
            this.totalElapsed = 0;
            this.totalElapsedQ = 0;

            //quantity of neurons which must be inserted by one thread
            int insertByOneThread = 100;

            SpeedStatistic.StartCounter("START ALL");
            for (int i = 0; i < threadsQuantity; i++)
            {
                TEST_10_insertNeuron((ulong)(i * insertByOneThread), (ulong)insertByOneThread,false);
            }

        }


        private void TEST_10_VisMid(SpeedStatistic.Counter cntr,string tableForCount)
        {

            totalElapsed += cntr.ElapsedMs;
            totalElapsedQ++;

            if (totalElapsedQ == threadsQuantity)
            {
                Console.WriteLine("MID time: {0};", totalElapsed / totalElapsedQ);

                //SpeedStatistic.StopCounter("START ALL");
                //SpeedStatistic.PrintOut("START ALL");

                var totalCntr = SpeedStatistic.GetCounter("START ALL");
                totalCntr.Stop();
                totalCntr.PrintOut();

                using (var tran = engine.GetTransaction())
                {
                    ulong nrnsCnt = tran.Count(tableForCount);
                    ulong speed = 0;

                    if (totalCntr.ElapsedMs>0)
                        speed = (ulong)((double)nrnsCnt * 1000 / (double)totalCntr.ElapsedMs);

                    Console.WriteLine("Total Nrns: {0}; Speed: {1} records/sec", nrnsCnt, speed);
                }

                SpeedStatistic.ClearAll();
            }
            //Console.WriteLine(totalElapsedQ);
        }

        private void TEST_10_insertNeuron(ulong startNeuron,ulong quantity, bool showSingleStat)
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    SpeedStatistic.StartCounter("INSERT NEURON " + startNeuron.ToString());
                    
                    for (ulong i = startNeuron; i < (quantity+startNeuron); i++)
                    {
                        tran.Insert<ulong, byte[]>("Neurons", i, null);
                        tran.Commit();                       
                    }

              
                    var cntr = SpeedStatistic.GetCounter("INSERT NEURON " + startNeuron.ToString());

                    if(showSingleStat)
                        SpeedStatistic.PrintOut("INSERT NEURON " + startNeuron.ToString());

                    SpeedStatistic.StopCounter("INSERT NEURON " + startNeuron.ToString());

                    TEST_10_VisMid(cntr, "Neurons");
                    


                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }
        #endregion



        private void TEST_9_3()
        {
           
            int totalTablesToCreate = 1000000;
            //int totalTablesToCreate = 200000;
            //int totalTablesToCreate = 34000;
            int calcStatEveryNtables = 10000;
            byte[] v = null;


            SpeedStatistic.StartCounter("ALL OPER");

            SpeedStatistic.StartCounter("Chunk");

            for (int i = 1; i <= totalTablesToCreate; i++)
            {

                using (var tran = engine.GetTransaction())
                {
                    try
                    {
                        var row = tran.Select<byte[], byte[]>("t" + i, new byte[] { 0 });

                        v = row.Value;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }

                }              

                if (i % calcStatEveryNtables == 0)
                {
                    SpeedStatistic.StopCounter("Chunk");
                    SpeedStatistic.PrintOut("Chunk", true);
                    SpeedStatistic.StartCounter("Chunk");
                }
            }


            SpeedStatistic.StopCounter("ALL OPER");
            SpeedStatistic.PrintOut("ALL OPER");
            SpeedStatistic.ClearAll();
            
        }

        private void TEST_9_2()
        {
        
            int totalTablesToCreate = 34000;
            int calcStatEveryNtables = 100;
            byte[] v = null;

            using (var tran = engine.GetTransaction())
            {
                SpeedStatistic.StartCounter("ALL OPER");
                try
                {
                    SpeedStatistic.StartCounter("Chunk");
                    for (int i = 1; i <= totalTablesToCreate; i++)
                    {

                        var row = tran.Select<byte[], byte[]>("t" + i, new byte[] { 1 });

                        v = row.Value;
                        
                        if (i % calcStatEveryNtables == 0)
                        {
                            SpeedStatistic.StopCounter("Chunk");
                            SpeedStatistic.PrintOut("Chunk", true);
                            SpeedStatistic.StartCounter("Chunk");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

                SpeedStatistic.StopCounter("ALL OPER");
                SpeedStatistic.PrintOut("ALL OPER");
                SpeedStatistic.ClearAll();
            }            
        }


        private void TEST_9_1()
        {
            
            int totalTablesToCreate = 34000;
            int calcStatEveryNtables = 100;

            Random rnd = new Random();
            int t = rnd.Next(10);
            t = t + 2;

            SpeedStatistic.StartCounter("ALL OPER");

            SpeedStatistic.StartCounter("Chunk");
            for (int i = 1; i <= totalTablesToCreate; i++)
            {
                using (var tran = engine.GetTransaction())
                {
                    try
                    {

                        tran.Insert<byte[], byte[]>("t" + i, new byte[] { (byte)t }, new byte[] { 0 });
                        tran.Commit();

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }

                if (i % calcStatEveryNtables == 0)
                {
                    SpeedStatistic.StopCounter("Chunk");
                    SpeedStatistic.PrintOut("Chunk", true);
                    SpeedStatistic.StartCounter("Chunk");
                }

            }

            SpeedStatistic.StopCounter("ALL OPER");
            SpeedStatistic.PrintOut("ALL OPER");
            SpeedStatistic.ClearAll();




        }

        private void TEST_9()
        {
            

            //int totalTablesToCreate = 34000;
            int totalTablesToCreate = 1000000;
            //int totalTablesToCreate = 200000;
            int calcStatEveryNtables = 100;

            SpeedStatistic.StartCounter("ALL OPER");

            //SpeedStatistic.StartCounter("Chunk");

            for (int i = 1; i <= totalTablesToCreate; i++)
            {
                using (var tran = engine.GetTransaction())
                {
                    try
                    {

                        tran.Insert<byte[], byte[]>("t" + i, new byte[] { 0 }, new byte[] { 0 });
                        tran.Commit();

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }               

                //if (i % calcStatEveryNtables == 0)
                //{
                //    //SpeedStatistic.StopCounter("Chunk");
                //    //SpeedStatistic.PrintOut("Chunk", true);
                //    //SpeedStatistic.StartCounter("Chunk");
                //}

            }

            SpeedStatistic.StopCounter("ALL OPER");
            SpeedStatistic.PrintOut("ALL OPER");
            SpeedStatistic.ClearAll();

           

            
        }

        private void TEST_8_9()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {


                    SpeedStatistic.StartCounter("SELECT");


                    int cnt = 0;
                    byte[] val = null;
                    DateTime dtSearch = new DateTime(1970, 7, 1);
                    //dtSearch = new DateTime(1971, 6, 1);
                    //dtSearch = new DateTime(1972, 1, 1);
                    //DateTime dtSearchStop = dtSearch.AddMonths(-1);
                    //foreach (var row in 
                    //    tran.SelectForwardSkipFrom<DateTime, byte[]>("t2", dtSearch, 1000000).Take(100000))
                    foreach (var row in
                        tran.SelectBackwardSkipFrom<DateTime, byte[]>("t2", dtSearch, 1000000).Take(100000))
                    {
                        val = row.Value;
                        cnt++;
                    }

                    Console.WriteLine(cnt);

                    SpeedStatistic.StopCounter("SELECT");

                    SpeedStatistic.PrintOut("SELECT");
                    SpeedStatistic.ClearAll();

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        private void TEST_8_8()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {


                    SpeedStatistic.StartCounter("SELECT");


                    int cnt = 0;
                    byte[] val = null;
                    DateTime dtSearch = new DateTime(1970, 7, 1);
                    //dtSearch = new DateTime(1971, 6, 1);
                    //dtSearch = new DateTime(1972, 1, 1);
                    DateTime dtSearchStop = dtSearch.AddMonths(-1);
                    //foreach (var row in tran.SelectBackwardFromTo<DateTime, byte[]>("t2", dtSearch, true, dtSearchStop, true).Take(100000))
                    //foreach (var row in tran.SelectForwardSkip<DateTime, byte[]>("t2", 9000000).Take(100000))
                    foreach (var row in tran.SelectBackwardSkip<DateTime, byte[]>("t2", 9000000).Take(100000))
                    {
                        //val = row.Value;
                        cnt++;
                    }

                    Console.WriteLine(cnt);

                    SpeedStatistic.StopCounter("SELECT");

                    SpeedStatistic.PrintOut("SELECT");
                    SpeedStatistic.ClearAll();

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        private void TEST_8_7()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {


                    SpeedStatistic.StartCounter("SELECT");


                    int cnt = 0;
                    byte[] val = null;
                    DateTime dtSearch = new DateTime(1970, 7, 1);
                    //dtSearch = new DateTime(1971, 6, 1);
                    //dtSearch = new DateTime(1972, 1, 1);
                    DateTime dtSearchStop = dtSearch.AddMonths(-1);
                    foreach (var row in tran.SelectBackwardFromTo<DateTime, byte[]>("t2", dtSearch, true, dtSearchStop, true).Take(100000))
                    {
                        val = row.Value;
                        cnt++;
                    }

                    Console.WriteLine(cnt);

                    SpeedStatistic.StopCounter("SELECT");

                    SpeedStatistic.PrintOut("SELECT");
                    SpeedStatistic.ClearAll();

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        private void TEST_8_6()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {


                    SpeedStatistic.StartCounter("SELECT");


                    int cnt = 0;
                    byte[] val = null;
                    DateTime dtSearch = new DateTime(1970, 7, 1);                    
                    dtSearch = new DateTime(1971, 6, 1);
                    dtSearch = new DateTime(1972, 1, 1);
                    DateTime dtSearchStop = dtSearch.AddMonths(1);
                    foreach (var row in tran.SelectForwardFromTo<DateTime, byte[]>("t2", dtSearch, true, dtSearchStop, true).Take(100000))
                    {
                        val = row.Value;
                        cnt++;
                    }

                    Console.WriteLine(cnt);

                    SpeedStatistic.StopCounter("SELECT");

                    SpeedStatistic.PrintOut("SELECT");
                    SpeedStatistic.ClearAll();

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        private void TEST_8_5()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {


                    SpeedStatistic.StartCounter("SELECT");
                                      

                    int cnt = 0;
                    byte[] val = null;
                    DateTime dtSearch = new DateTime(1970,7,1);
                    //dtSearch = new DateTime(1971, 6, 1);
                    dtSearch = new DateTime(1972, 1, 1);
                    foreach (var row in tran.SelectForwardStartFrom<DateTime, byte[]>("t2", dtSearch,true).Take(100000))
                    {
                        val = row.Value;
                        cnt++;
                    }

                    Console.WriteLine(cnt);

                    SpeedStatistic.StopCounter("SELECT");

                    SpeedStatistic.PrintOut("SELECT");
                    SpeedStatistic.ClearAll();

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }


        private void TEST_8()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {


                    SpeedStatistic.StartCounter("SELECT");

                    int cnt = 0;
                    byte[] val = null;
                    //foreach (var row in tran.SelectBackward<DateTime, byte[]>("t2").Take(1000000))
                    foreach (var row in tran.SelectForward<byte[], byte[]>("t2").Take(1000000))
                    {
                        //val = row.Value;
                        cnt++;
                    }

                    Console.WriteLine(cnt);

                    SpeedStatistic.StopCounter("SELECT");

                    SpeedStatistic.PrintOut("SELECT");
                    SpeedStatistic.ClearAll();

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }


        private void TEST_7()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {


                    SpeedStatistic.StartCounter("INSERT RANDOM");
                    int vl = 0;
                    Random rnd = new Random();
                    for (int i = 0; i < 100000; i++)
                    {
                        vl = rnd.Next(1000000);

                        tran.Insert<int, byte[]>("t5", vl, null);
                        //tran.Commit();
                    }

                    tran.Commit();
                        

                    SpeedStatistic.StopCounter("INSERT RANDOM");

                    SpeedStatistic.PrintOut("INSERT RANDOM");
                    SpeedStatistic.ClearAll();

                    //FileInfo fi=new FileInfo(@"D:\temp\DBreezeBenchmark\10000002");
                    //Console.WriteLine(fi.Length);

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        private void TEST_6()
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {
                                
                    
                    SpeedStatistic.StartCounter("COPY");
                    int cnt = 0;
                    foreach(var row in tran.SelectForward<int,byte[]>("t3"))
                    {
                        //!!!!!!!!!!!!!!!   HERE we make HDD head to move too much R-W-R-W...better read first chunk 1MLN in memory then save etc till the end R-R-R-W-R-R-R-W
                        tran.Insert<int, byte[]>("t4", row.Key, row.Value);
                        cnt++;
                    }

                    tran.Commit();

                    SpeedStatistic.StopCounter("COPY");

                    SpeedStatistic.PrintOut("COPY");


                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        private void TEST_5_STARTER()
        {
            for (int i = 64; i < 100; i++)
            {
                TEST_5(i);
            }
        }


        private void TEST_5(int num)
        {
            using (var tran = engine.GetTransaction())
            {
                try
                {
                    //SpeedStatistic.StartCounter("S ");
                    //var cnt = tran.Count("t3");
                    //tran.Select<int, byte[]>("t3",10);
                    //SpeedStatistic.StopCounter("S ");
                    //SpeedStatistic.PrintOut("S ");

                    SpeedStatistic.StartCounter("INSERT "+num);

                    int k = num;

                    for (int i = 0; i < 1000000; i++)                   
                    {
                        tran.Insert<int, byte[]>("t3", k, null);
                        k += 255;
                    }

                    tran.Commit();             

                    SpeedStatistic.StopCounter("INSERT "+num);

                    SpeedStatistic.PrintOut("INSERT "+num);


                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }


        /// <summary>
        /// Opening and deleting file
        /// </summary>
        private void TEST_4()
        {
            FileStream _fs = null;
            string fn = Path.Combine(_folder, "xxx.rol");

            SpeedStatistic.StartCounter("CREATE FILE");
            for (int i = 0; i < 10000; i++)
            {
                _fs = new FileStream(fn, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                _fs.Close();

                File.Delete(fn);
            }

            SpeedStatistic.StopCounter("CREATE FILE");
            SpeedStatistic.PrintOut(true);
        }


        #region "TEST_3_1"

        //private void TEST_3_1_STARTER()
        //{
        //    StartInThread1();
        //    StartInThread2();
        //    StartInThread3();
        //    StartInThread4();
        //    StartInThread5();
        //}

        private void Start312(string tableName)
        {
            Action a = () =>
            {
                TEST_3_1(tableName);
            };

            a.DoAsync();
        }

        int threadsQuantity = 100;
        int recsQuantity2Insert = 1000000;

        private void TEST_3_1_STARTER_2()
        {
            threadsQuantity = 100;
            threadsQuantity = 6;

            SpeedStatistic.StartCounter("START ALL");
            for (int i = 0; i < threadsQuantity; i++)
            {
                Start312("t3" + i.ToString());
            }
           
        }

        private long totalElapsed = 0;
        private long totalElapsedQ = 0;
        private void VisMid(SpeedStatistic.Counter cntr)
        {
            
            totalElapsed += cntr.ElapsedMs;
            totalElapsedQ++;

            if (totalElapsedQ == threadsQuantity)
            {
                Console.WriteLine("MID: {0};", totalElapsed / totalElapsedQ);
                SpeedStatistic.StopCounter("START ALL");
                SpeedStatistic.PrintOut("START ALL");
            }
            //Console.WriteLine(totalElapsedQ);
        }

        /// <summary>
        /// Key dt with good jump, commit after every insert
        /// </summary>
        private void TEST_3_1(string tableName)
        {
            //engine.Schema.DeleteTable(tableName);

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    SpeedStatistic.StartCounter("INSERT" + tableName);

                    DateTime dt = new DateTime(1970, 1, 1);

                   
                    for (int i = 0; i < recsQuantity2Insert; i++)
                    {
                        tran.Insert<DateTime, byte[]>(tableName, dt, null);
                        dt = dt.AddSeconds(7);
                        //tran.Commit();
                    }

                    tran.Commit();

                    //Console.WriteLine("LastDt: {0}", dt.ToString("dd.MM.yyyy HH:mm:ss"));

                    SpeedStatistic.StopCounter("INSERT" + tableName);

                    var cntr = SpeedStatistic.GetCounter("INSERT" + tableName);

                    VisMid(cntr);
                    //SpeedStatistic.PrintOut("INSERT" + tableName);


                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        #endregion

        #region "TEST_3_2_Update"

        private void TEST_3_2()
        {

            //engine.Schema.DeleteTable("t3");

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    SpeedStatistic.StartCounter("UPDATE");

                    DateTime dt = new DateTime(1970, 1, 1);

                    
                    for (int i = 0; i < 100000; i++)
                    {
                        tran.Insert<DateTime, byte[]>("t3", dt, null);
                        dt = dt.AddSeconds(7);
                        //tran.Commit();
                    }

                    tran.Commit();

                    Console.WriteLine("LastDt: {0}", dt.ToString("dd.MM.yyyy HH:mm:ss"));

                    SpeedStatistic.StopCounter("UPDATE");

                    SpeedStatistic.PrintOut(true);

                    //LastDt: 23.03.1970 00:26:40

                    //U: 100K; FS: 2.9MB; 34 s; Commit After Every Update
                    //U: 1MLN; FS: 29MB; 356 s; Commit After Every Update

                    //U: 100K; FS: 2.9MB; 2.8 s; Commit After All Updates
                    //U: 1MLN; FS: 29MB; 28 s; Commit After All Updates

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        #endregion

        /// <summary>
        /// Key dt with good jump, commit after every insert
        /// </summary>
        private void TEST_3()
        {
            
            engine.Scheme.DeleteTable("t3");

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    SpeedStatistic.StartCounter("INSERT");

                    DateTime dt = new DateTime(1970, 1, 1);

                    for (int i = 0; i < 100000; i++)
                    //for (int i = 0; i < 1000000; i++)
                    {
                        tran.Insert<DateTime, byte[]>("t3", dt, null);
                        dt = dt.AddSeconds(7);
                        tran.Commit();
                    }

                    //tran.Commit();

                    Console.WriteLine("LastDt: {0}", dt.ToString("dd.MM.yyyy HH:mm:ss"));

                    SpeedStatistic.StopCounter("INSERT");

                    SpeedStatistic.PrintOut(true);

                    //LastDt: 23.03.1970 00:26:40
                    //I: 100K; FS: 2.9MB; 19 s
                    //I: 1MLN; FS: 29MB; 212 s

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }


        private void TEST_2()
        {
            engine.Scheme.DeleteTable("t2");

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    SpeedStatistic.StartCounter("INSERT");

                    DateTime dt = new DateTime(1970, 1, 1);

                    for (int i = 0; i < 1000000; i++)
                    //for (int i = 0; i < 1000000; i++)
                    //for (int i = 0; i < 100000; i++)
                    {
                        tran.Insert<DateTime, byte[]>("t2", dt, null);
                        dt = dt.AddSeconds(7);
                    }

                    tran.Commit();

                    Console.WriteLine("LastDt: {0}", dt.ToString("dd.MM.yyyy HH:mm:ss"));

                    SpeedStatistic.StopCounter("INSERT");

                    SpeedStatistic.PrintOut(true);

                    //LastDt: 23.03.1970 00:26:40
                    //100K
                    //FS: 2.2MB, 997 ms
                    //10MLN
                    //FS: 220MB, 98 sec
                    //1mln
                    //INSERT: 1; Time: 9966 ms; 27233088 ticks 
                    //FS: 22MB
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }





        private void TEST_1_9()
        {
          

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    SpeedStatistic.StartCounter("SELECT DOT");

                    Random rnd = new Random();
                    int key = 0;
                    byte[] val = null;
                    int cc = 0;

                    //var row1 = tran.Select<int, byte[]>("t1", 983290);
                    //if (row1.Exists)
                    //{
                    //    //val = row1.Value;
                    //}
                    //else
                    //{
                    //    cc++;
                    //    Console.WriteLine("not found key: " + key);
                    //}


                    for (int i = 0; i < 100000; i++)
                    {
                        key = rnd.Next(999999);
                        //key = i;
                        var row = tran.Select<int, byte[]>("t1", key);
                        if (row.Exists)
                        {
                            val = row.Value;
                        }
                        else
                        {
                            cc++;
                            Console.WriteLine("not found key: " + key);
                        }

                    }

                    Console.WriteLine(cc);
                    SpeedStatistic.StopCounter("SELECT DOT");

                    SpeedStatistic.PrintOut(true);
                    
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        

        private void TEST_1()
        {
            engine.Scheme.DeleteTable("t1");

            using (var tran = engine.GetTransaction())
            {
                try
                {
                    SpeedStatistic.StartCounter("INSERT");

                    for (int i = 0; i < 1000000; i++)
                    {
                        tran.Insert<int, byte[]>("t1", i, null);
                    }

                    tran.Commit();
                    SpeedStatistic.StopCounter("INSERT");

                    SpeedStatistic.PrintOut(true);

                    //INSERT: 1; Time: 9256 ms; 25292025 ticks 
                    //FS: 18MB
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        


    }
}
