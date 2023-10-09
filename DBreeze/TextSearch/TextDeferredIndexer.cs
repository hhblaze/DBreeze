/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using DBreeze.LianaTrie;
using DBreeze.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;

using DBreeze.Utils;

namespace DBreeze.TextSearch
{
    /// <summary>
    /// TextDeferredIndexer
    /// </summary>
    internal class TextDeferredIndexer:IDisposable
    {
        DBreezeEngine DBreezeEngine = null;
        TrieSettings LTrieSettings = null;
        IStorage Storage = null;
        LTrie LTrie = null;
        static string TableFileName = "_DBreezeTextIndexer";
        object lock_operation = new object();
        long init = DateTime.UtcNow.Ticks;
        int inDeferredIndexer = 0;
        int disposed = 0;

        public TextDeferredIndexer(DBreezeEngine engine)
        {
            this.DBreezeEngine = engine;
            LTrieSettings = new TrieSettings()
            {
                InternalTable = true
            };
            Storage = new StorageLayer(Path.Combine(engine.MainFolder, TableFileName), LTrieSettings, engine.Configuration);
            LTrie = new LTrie(Storage);
            LTrie.TableName = "DBreeze.TextIndexer";

            if (LTrie.Storage.Length > 100000)  //Recreating file if its size more then 100KB and it is empty
            {
                if (LTrie.Count(true) == 0)
                {
                    LTrie.Storage.RecreateFiles();
                    LTrie.Dispose();

                    Storage = new StorageLayer(Path.Combine(engine.MainFolder, TableFileName), LTrieSettings, engine.Configuration);
                    LTrie = new LTrie(Storage);
                    LTrie.TableName = "DBreeze.TextIndexer";
                }
            }

            if (LTrie.Count(true) > 0)
                this.StartDefferedIndexing();
        }

        /// <summary>
        /// Disposing
        /// </summary>
        public void Dispose()
        {
            if (System.Threading.Interlocked.CompareExchange(ref disposed, 1, 0) != 0)
                return;

            if (LTrie != null)
            {
                LTrie.Dispose();
            }
        }

        /// <summary>
        /// Add tables and their InternalDocumentIDs for paraller indexing of the Text Engine
        /// </summary>
        /// <param name="defferedDocIds"></param>
        public void Add(Dictionary<string, HashSet<uint>> defferedDocIds)
        {
            if (defferedDocIds == null || defferedDocIds.Count == 0)
                return;

            lock (lock_operation)
            {
                init++;
                var bt = DBreeze.Utils.Biser.Encode_DICT_PROTO_STRING_UINTHASHSET(defferedDocIds, Compression.eCompressionMethod.NoCompression);                
                LTrie.Add(init.To_8_bytes_array_BigEndian(), bt);
                LTrie.Commit();
            }
        }

        /// <summary>
        /// Add tables and their InternalDocumentIDs for paraller indexing of the Vector Engine, protocol 0,0
        /// </summary>
        /// <param name="defferedDocIds"></param>
        public void AddVectors(Dictionary<string, HashSet<uint>> defferedDocIds)
        {
            if (defferedDocIds == null || defferedDocIds.Count == 0)
                return;

            lock (lock_operation)
            {
                init++;
                var bt = DBreeze.Utils.Biser.Encode_DICT_PROTO_STRING_UINTHASHSET(defferedDocIds, Compression.eCompressionMethod.NoCompression);
                LTrie.Add(new byte[] { 0, 0 }.Concat(init.To_8_bytes_array_BigEndian()), bt);
                LTrie.Commit();
            }
        }

        /// <summary>
        /// Runs Indexer. Only one instance is allowed
        /// </summary>
        public void StartDefferedIndexing()
        {
            if (System.Threading.Interlocked.CompareExchange(ref inDeferredIndexer, 1, 0) != 0)
                return;

            //new System.Threading.Thread(new System.Threading.ThreadStart(() =>
            //{
            //    Indexer();
            //})).Start();         

#if NET35 || NETr40   //The same must be use for .NET 4.0

            new System.Threading.Thread(new System.Threading.ThreadStart(() =>
            {
                this.DBreezeEngine.BackgroundNotify("TextDefferedIndexingHasStarted", null);
                Indexer();
                this.DBreezeEngine.BackgroundNotify("TextDefferedIndexingHasFinished", null);
            })).Start(); 
#else
            System.Threading.Tasks.Task.Run(() => {
                this.DBreezeEngine.BackgroundNotify("TextDefferedIndexingHasStarted", null);
                Indexer();
                this.DBreezeEngine.BackgroundNotify("TextDefferedIndexingHasFinished", null);
            });
#endif
        }




        /// <summary>
        /// Indexer self
        /// </summary>
        void Indexer()
        {

            int maximalIterations = 10; //Iterations then a breath
            int currentItter = 0;

            int maximalVectorsPerRound = 500; //for all tables

            Dictionary<byte[], Dictionary<string, HashSet<uint>>> defTasks = new Dictionary<byte[], Dictionary<string, HashSet<uint>>>();
            Dictionary<string, HashSet<uint>> defTask = null;
            Dictionary<string, TextSearchHandler.ITS> itbls = new Dictionary<string, TextSearchHandler.ITS>();


            Dictionary<byte[], Dictionary<string, HashSet<uint>>> defVecors = new Dictionary<byte[], Dictionary<string, HashSet<uint>>>();

            while (true)
            {
                currentItter = 0;

                defTasks.Clear();
                itbls.Clear();

                defVecors.Clear();

                lock (lock_operation)
                {
                    //-other indexer
                    bool otherIndexersFound= false;
                    bool goOn = true;

                    int vectorsCount = 0;

                    foreach (var row in LTrie.IterateForwardStartsWith(new byte[] { 0 }, true, false))
                    {
                        /*
                         * protocol:
                         * byte[0] = 0, means the other than TextIndexer
                         * byte[1] represent type of deffered indexer, i.e byte[1] = 0 - means VectorStorage deferred indexer
                         */
                        switch(row.Key[1])
                        {
                            case 0: //VectorEngine
                                var defVectorIDs =  new Dictionary<string, HashSet<uint>>();
                                DBreeze.Utils.Biser.Decode_DICT_PROTO_STRING_UINTHASHSET(row.GetFullValue(true), defVectorIDs, Compression.eCompressionMethod.NoCompression);
                                defVecors.Add(row.Key, defVectorIDs);
                                vectorsCount += defVectorIDs.Count;
                                if(vectorsCount > maximalVectorsPerRound)
                                    goOn = false;
                                break;
                            default:
                                throw new Exception("Unknown protocol in TextDeferredIndexer.Indexer()");
                                
                        }


                        //used by all indexers
                        otherIndexersFound = true;
                        currentItter++;

                        if (!goOn)
                            break;
                    }


                    //-Text Indexer 
                    if(!otherIndexersFound)
                    {
                        //doesn't start until other indexers are finished
                        //LTrie must be empty from 0 protocol used by the other indexers

                        foreach (var row in LTrie.IterateForward(true, false).Take(maximalIterations))
                        {

                            currentItter++;
                            defTask = new Dictionary<string, HashSet<uint>>();
                            DBreeze.Utils.Biser.Decode_DICT_PROTO_STRING_UINTHASHSET(row.GetFullValue(true), defTask, Compression.eCompressionMethod.NoCompression);
                            defTasks.Add(row.Key, defTask);

                            foreach (var el in defTask)
                            {
                                if (!itbls.ContainsKey(el.Key))
                                    itbls[el.Key] = new TextSearchHandler.ITS();

                                foreach (var el1 in el.Value)
                                    itbls[el.Key].ChangedDocIds.Add((int)el1);

                            }
                        }
                    }
                   

                    if (currentItter == 0)
                    {
                        inDeferredIndexer = 0;  //going out
                        return;
                    }
                }

                //----------------------  INDEXING

                //-Indexing Vector Engine
                if(defVecors.Count > 0)
                {
                    //K: tableName, Value all vectors internalIDs to be indexed
                    Dictionary<string, HashSet<int>> dVectors=new Dictionary<string, HashSet<int>>();
                    foreach(var el in defVecors)
                    {
                        foreach(var tv in el.Value)
                        {
                            if(!dVectors.TryGetValue(tv.Key, out var hs))
                            {
                                dVectors[tv.Key] = new HashSet<int>();
                            }

                            foreach (var hsi in tv.Value)
                                dVectors[tv.Key].Add((int)hsi);
                        }
                    }

                    using (var tran = this.DBreezeEngine.GetTransaction())
                    {
                        
                       tran.SynchronizeTables(dVectors.Keys.ToList());

                        foreach (var el in dVectors)
                            tran.VectorsDoIndexing(el.Key, el.Value.OrderBy(r=>r).ToList());

                        tran.Commit();
                        //dVectors will be cleaned on while start                        
                    }

                    //Removing indexed docs from LTrie
                    lock (lock_operation)
                    {
                        byte[] key = null;

                        foreach (var el in defVecors.OrderBy(r => r.Key.ToBytesString()))
                        {
                            key = el.Key;
                            LTrie.Remove(ref key);
                        }

                        LTrie.Commit();
                    }
                }


                //- Indexing TextEngine defTasks (Text Engine)
                if (defTasks.Count > 0)
                {
                   
                    using (var tran = this.DBreezeEngine.GetTransaction())
                    {
                        tran.tsh = new TextSearchHandler(tran);
                        tran.SynchronizeTables(itbls.Keys.ToList());
                        tran.tsh.DoIndexing(tran, itbls);
                        tran.Commit();

                        itbls.Clear();
                    }

                    //Removing indexed docs from LTrie
                    lock (lock_operation)
                    {
                        byte[] key = null;

                        foreach (var el in defTasks.OrderBy(r => r.Key.ToBytesString()))
                        {
                            key = el.Key;
                            LTrie.Remove(ref key);
                        }

                        LTrie.Commit();
                    }
                }


            }//eo while



        }//eof

    }//eoc
}
