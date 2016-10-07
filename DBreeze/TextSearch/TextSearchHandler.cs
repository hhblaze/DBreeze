/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;

using DBreeze;
using DBreeze.Transactions;
using DBreeze.Utils;
using DBreeze.DataTypes;
using System.Diagnostics;


namespace DBreeze.TextSearch
{
    /// <summary>
    /// New instance per transaction. Is created by necessity, while inserting or selecting anything concerning TextSearch subsystem.
    /// </summary>
    internal class TextSearchHandler
    {
        public bool InsertWasPerformed = false;
        Transaction tran = null;
        Dictionary<string, HashSet<uint>> defferedDocIds = new Dictionary<string, HashSet<uint>>();

        public TextSearchHandler(Transaction tran)
        {
            this.tran = tran;
        }
       
        /// <summary>
        /// Internal search-table structure.       
        /// </summary>
        internal class ITS
        {
            public HashSet<int> ChangedDocIds = new HashSet<int>();            

            /// <summary>
            /// External document index to internal - 1. Key byte[], Value int
            /// </summary>
            public NestedTable e2i = null;
            /// <summary>
            /// Internal document index to external - 2. Key int, Value byte[]
            /// </summary>
            public NestedTable i2e = null;
            /// <summary>
            /// Searchables to insert - 3. internal docId(int)+ new byte[]{0}/new byte[]{1} (0 for current searchables, 1 for new intended to be saved searchables), Value is searchables.
            /// Insert always compares newly intended with current and if no changes exits. 
            /// Indexer replaces new with current.
            /// itbls.Value.ChangedDocIds contains IDs of changed docs per search table
            /// </summary>
            public NestedTable srch = null;

            //Key 4: LastIndexedTime tran.Select<byte, byte[]>(tbl, 4); Under index 4 we hold LastIndexedTime for that table   

            /// <summary>
            /// Key 10: [uint,byte[]] where K is BlockID[uint] (1000 words per block), Value is GzippedAndProtobufed Dictionary of [uint, byte[]] where K is ID of the word in Key2 and value its WAH reserved (init reservation 100KB per block)            
            /// </summary>
            public NestedTable blocks = null;

            /// <summary>
            /// Key 11: [int] - current blockNumber
            /// </summary>
            public uint currentBlock = 0;

            /// <summary>
            /// Key 12: [int] used number in the block
            /// </summary>
            public uint numberInBlock = 0;
            /// <summary>
            /// Key 20 : [string,byte[]] NestedTable(Index to search by words)
            /// <para>Where Key: string - word</para>
            /// <para>Value: [byte[]] BlockId[uint] + NumberInBlock[uint] (reference to Key 10)</para>
            /// </summary>
            public NestedTable words = null;
        }

        /// <summary>
        /// Registering all search-tables mutated during transaction
        /// </summary>
        Dictionary<string, ITS> itbls = new Dictionary<string, ITS>();

        public enum eInsertMode
        {
            Insert,
            Append,
            Remove
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tran"></param>
        /// <param name="tableName">Search document space/physical dbreeze table, that's why must be synchronized</param>
        /// <param name="documentId"></param>
        /// <param name="searchables"></param>
        /// <param name="opt"></param>
        /// <param name="iMode"></param>
        public void InsertDocumentText(Transaction tran, string tableName, byte[] documentId, string searchables, TextSearchStorageOptions opt, eInsertMode iMode)
        {
            
            //tran._transactionUnit.TransactionsCoordinator._engine.Configuration.
            if (String.IsNullOrEmpty(tableName) || documentId == null)
                return;

            if ((iMode == eInsertMode.Append || iMode == eInsertMode.Remove) && String.IsNullOrEmpty(searchables))
                return;

            SortedDictionary<string, WordDefinition> pST = this.GetWordsDefinitionFromText(searchables, opt); //flattend searchables
            StringBuilder sbPs = new StringBuilder();

            //Registering all tables for text-search in current transaction
            ITS its = null;
            if (!itbls.TryGetValue(tableName, out its))
            {
                its = new ITS()
                {
                    e2i = tran.InsertTable<byte>(tableName, 1, 0),
                    i2e = tran.InsertTable<byte>(tableName, 2, 0),
                    srch = tran.InsertTable<byte>(tableName, 3, 0),                    
                };

                its.e2i.ValuesLazyLoadingIsOn = false;
                its.i2e.ValuesLazyLoadingIsOn = false;
                its.srch.ValuesLazyLoadingIsOn = false;

                itbls.Add(tableName, its);
            }

            //Internal document ID
            int iId = 0;
            
            //Searching document by externalID
            var r1 = its.e2i.Select<byte[], int>(documentId);            

            if (r1.Exists)          //DOCUMENT EXISTS
            {                
                iId = r1.Value;                

                //Getting old searchables for this document                
                byte[] oldSrch = its.srch.Select<byte[], byte[]>(iId.To_4_bytes_array_BigEndian().Concat(new byte[] { 0 }), true).Value;
                HashSet<string> oldSearchables = GetSearchablesFromByteArray_AsHashSet(oldSrch); //always instantiated hashset

                switch (iMode)
                {
                    case eInsertMode.Insert:
                        //Comparing 
                        if (oldSearchables.Intersect(pST.Keys).Count() == oldSearchables.Count && oldSearchables.Count == pST.Keys.Count)
                            return; //Going out, nothing to insert

                        foreach (var ps1i in pST)
                        {
                            sbPs.Append(ps1i.Key);
                            sbPs.Append(" ");
                        }
                        break;
                    case eInsertMode.Append:
                    case eInsertMode.Remove:

                        if ((iMode == eInsertMode.Append) 
                            && 
                            oldSearchables.Intersect(pST.Keys).Count() == oldSearchables.Count 
                            &&
                            oldSearchables.Count == pST.Keys.Count
                            )
                            return; //Going out, nothing to insert

                        foreach (var ew in pST.Keys)
                        {
                            if (iMode == eInsertMode.Append)
                                oldSearchables.Add(ew);
                            else
                                oldSearchables.Remove(ew);
                        }

                        foreach (var el in oldSearchables)
                        {
                            sbPs.Append(el);
                            sbPs.Append(" ");
                        }
                        
                        break;
                }
            }
            else
            {
                //DOCUMENT NEW
                if (pST.Count < 1)
                    return; //Going out, nothing to insert

                //Document is new
                if (iMode == eInsertMode.Append)
                    iMode = eInsertMode.Insert;
                else if (iMode == eInsertMode.Remove)
                    return; //Going out
                iId = its.i2e.Max<int, byte[]>().Key;
                iId++;

                its.e2i.Insert<byte[], int>(documentId, iId); 
                its.i2e.Insert<int, byte[]>(iId, documentId);

                foreach (var ps1i in pST)
                {
                    sbPs.Append(ps1i.Key);
                    sbPs.Append(" ");
                }
            }
            
            this.InsertWasPerformed = true;

            //Inserting into affected table
            if (!opt.DeferredIndexing)
                its.ChangedDocIds.Add(iId);
            else
            {
                if (!defferedDocIds.ContainsKey(tableName))
                    defferedDocIds[tableName] = new HashSet<uint>();

                defferedDocIds[tableName].Add((uint)iId);
            }
            
            //Inserting searchables to be indexed            
            its.srch.Insert<byte[], byte[]>(iId.To_4_bytes_array_BigEndian().Concat(new byte[] { 1}), GetByteArrayFromSearchbles(sbPs.ToString()));                   
        }

       

        class WordInDocs
        {            
            public uint BlockId = 0;
            public uint NumberInBlock = 0;         
            public int foundOrigin = 0;
            public WAH2 wah = null;            
        }


        /// <summary>
        /// SearchTextInDocuments
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="req"></param>
        /// <returns></returns>
        public TextSearchResponse SearchTextInDocuments(string tableName, TextSearchRequest req)
        {
            TextSearchResponse resp = new TextSearchResponse();

            //[string,byte[]] BlockId[int] + NumberInBlock[int] 
            NestedTable tbWords = tran.SelectTable<byte>(tableName, 20, 0);
            tbWords.ValuesLazyLoadingIsOn = false;

            var Words = this.PrepareSearchKeyWords(req.SearchWords);

            #region "Multiple Words"

            int j = -1;
            List<byte[]> foundArrays = new List<byte[]>();
            List<byte[]> oneWordFoundArrays = new List<byte[]>();

            bool anyWordFound = false;
            int totalFoundWords = 0;

            Dictionary<string, WordInDocs> words = new Dictionary<string, WordInDocs>();
            int foundOrigin = 1;

            Dictionary<string, WordInDocs> perWord = new Dictionary<string, WordInDocs>();
            Dictionary<string, WordInDocs> firstHighOccuranceWord = new Dictionary<string, WordInDocs>();

            //Currently we ignore these words and do nothing with them
            List<string> highOccuranceWordParts = new List<string>();

            
            foreach (var word in Words.Take(tran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.MaxQuantityOfWordsToBeSearched)) //Maximum 10 words for search
            {
                anyWordFound = false;
                totalFoundWords = 0;
                perWord = new Dictionary<string, WordInDocs>();


                foreach (var row1 in tbWords.SelectForwardStartsWith<string, byte[]>(word))
                {
                    anyWordFound = true;
                    totalFoundWords++;

                    if (Words.Count() == 1 && totalFoundWords > req.Quantity)
                    {
                        //In case if only one search word, then we don't need to make any comparation
                        break;
                    }
                    else if (totalFoundWords >= req.NoisyQuantity)  //Found lots of words with such mask inside
                    {
                        //Too much found docs have this word-part inside, better to enhance search
                        if (firstHighOccuranceWord.Count() == 0)
                        {
                            //Only first HighOccurance word part come to the list. It can be used later in case if all search words are of HighOccurance (then we will visualize only this one)
                            firstHighOccuranceWord = perWord.ToDictionary(r => r.Key, r => r.Value);
                        }
                        //Clearing repack element
                        perWord.Clear();
                        //Adding word into List of High-Occurance word-part
                        highOccuranceWordParts.Add(word);
                        break;
                    }

                    perWord.Add(row1.Key, new WordInDocs()
                    {
                        BlockId = row1.Value.Substring(0, 4).To_UInt32_BigEndian(),
                        NumberInBlock = row1.Value.Substring(4, 4).To_UInt32_BigEndian(),
                        foundOrigin = foundOrigin
                    });
                }

                //Repacking occurances
                foreach (var pw in perWord)
                    words.Add(pw.Key, pw.Value);

                foundOrigin++;

                if (
                    req.SearchLogicType == TextSearchRequest.eSearchLogicType.AND
                    &&
                    !anyWordFound
                    )
                {
                    //Non of words found corresponding to AND logic                    
                    return resp;
                }
            }


            if (words.Count() == 0)
            {
                //In case of multiple search words and each of them of HighOccurance.
                //We will form result only from the first HighOccurance list

                //Repacking occurances
                foreach (var pw in firstHighOccuranceWord.Take(req.Quantity))
                    words.Add(pw.Key, pw.Value);

                //In this case highOccuranceWordParts must be cleared, because the returning result is very approximate
                highOccuranceWordParts.Clear();
            }


            //Here we must start get data from blocks
            //Nested table with blocks      
            //[uint,byte[]] where K is BlockID[uint]
            NestedTable tbBlocks = tran.SelectTable<byte>(tableName, 10, 0);
            tbBlocks.ValuesLazyLoadingIsOn = false;

            Dictionary<uint, byte[]> block = new Dictionary<uint, byte[]>();
            byte[] btBlock = null;
            uint currentBlockId = 0;

            //DBreeze.Diagnostic.SpeedStatistic.StartCounter("LoadBlocks");

            foreach (var wrd in words.OrderBy(r => r.Value.BlockId))
            {
                if (currentBlockId != wrd.Value.BlockId)
                {
                    currentBlockId = wrd.Value.BlockId;
                    block = new Dictionary<uint, byte[]>();

                    //DBreeze.Diagnostic.SpeedStatistic.StartCounter("SelectBlocks");
                    btBlock = tbBlocks.Select<uint, byte[]>(wrd.Value.BlockId).Value;
                    //DBreeze.Diagnostic.SpeedStatistic.StopCounter("SelectBlocks");                            
                    btBlock = btBlock.Substring(4, btBlock.Substring(0, 4).To_Int32_BigEndian());
                    //DBreeze.Diagnostic.SpeedStatistic.StartCounter("DecomDeserBlocks");
                    btBlock.Decode_DICT_PROTO_UINT_BYTEARRAY(block, Compression.eCompressionMethod.Gzip);
                    // block = btBlock.DeserializeProtobuf<Dictionary<int, byte[]>>();
                    //DBreeze.Diagnostic.SpeedStatistic.StopCounter("DecomDeserBlocks");
                }

                wrd.Value.wah = new WAH2(block[wrd.Value.NumberInBlock]);
            }
            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("LoadBlocks", true);
            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("SelectBlocks", true);
            //DBreeze.Diagnostic.SpeedStatistic.PrintOut("DecomDeserBlocks", true);

            foundOrigin = 0;

            foreach (var wrd in words.OrderBy(r => r.Value.foundOrigin))
            {
                //Console.WriteLine(wrd.Value.foundOrigin);

                if (foundOrigin != wrd.Value.foundOrigin)
                {
                    if (oneWordFoundArrays.Count() > 0)
                    {
                        j++;
                        foundArrays.Add(WAH2.MergeAllUncompressedIntoOne(oneWordFoundArrays));
                        oneWordFoundArrays = new List<byte[]>();
                    }

                    foundOrigin = wrd.Value.foundOrigin;
                }
                else
                {

                }

                oneWordFoundArrays.Add(wrd.Value.wah.GetUncompressedByteArray());
            }

            //The last 
            if (oneWordFoundArrays.Count() > 0)
            {
                j++;
                foundArrays.Add(WAH2.MergeAllUncompressedIntoOne(oneWordFoundArrays));
                oneWordFoundArrays = new List<byte[]>();
            }


            //////////  final results

            if (j >= 0)
            {
                var q = WAH2.TextSearch_OR_logic(foundArrays, req.Quantity);

                if (req.SearchLogicType == TextSearchRequest.eSearchLogicType.AND)
                    q = WAH2.TextSearch_AND_logic(foundArrays).Take(req.Quantity);

                //Key int, Value byte[]
                NestedTable i2e = tran.SelectTable<byte>(tableName, 2, 0);
                i2e.ValuesLazyLoadingIsOn = false;

                int qOutput = 0;
                DBreeze.DataTypes.Row<int, byte[]> docRow = null;
                foreach (var el in q)
                {
                    ////Getting document exterrnal ID
                    docRow = i2e.Select<int, byte[]>((int)el);
                    if (docRow.Exists)
                        resp.FoundDocumentIDs.Add(docRow.Value);

                    //docRow = dt.Select<int, byte[]>((int)el);
                    //if (docRow.Exists)
                    //{
                    //    if (!dmnts.ContainsKey((int)el))
                    //    {
                    //        if (highOccuranceWordParts.Count() > 0)
                    //        {
                    //            //We got some noisy word-parts of high occurance together with strongly found words.
                    //            //We must be sure that these word parts are also inside of returned docs
                    //            doc = this.RetrieveDocument(req.IncludeDocumentsContent, true, dt, docRow);
                    //            if (doc != null)
                    //            {
                    //                //Checking doc.Searchables must have all word parts from the occurance in case of AND
                    //                if (req.SearchLogicType == SearchRequest.eSearchLogicType.AND)
                    //                {
                    //                    if (String.IsNullOrEmpty(doc.Searchables))
                    //                        continue;
                    //                    if (!highOccuranceWordParts.All(doc.Searchables.ToLower().Contains))
                    //                        continue;
                    //                }

                    //                if (req.IncludeDocuments)
                    //                {
                    //                    if (!req.IncludeDocumentsSearchanbles)
                    //                        doc.Searchables = String.Empty;

                    //                    dmnts.Add((int)el, doc);
                    //                }
                    //                else
                    //                {
                    //                    dmnts.Add((int)el, null);
                    //                }

                    //            }
                    //            else
                    //                continue;
                    //        }
                    //        else
                    //        {
                    //            if (req.IncludeDocuments)
                    //            {

                    //                doc = this.RetrieveDocument(req.IncludeDocumentsContent, req.IncludeDocumentsSearchanbles, dt, docRow);
                    //                if (doc == null) //If doc is deleted, while search was in progress and we received its id in the list
                    //                    continue;

                    //                dmnts.Add((int)el, doc);
                    //            }
                    //            else
                    //            {
                    //                dmnts.Add((int)el, null);
                    //            }

                    //        }

                    //        qOutput++;
                    //    }
                    //}

                    qOutput++;

                    if (qOutput > req.Quantity)
                        break;

                }

            }
            #endregion



            return resp;
        }

        /// <summary>
        /// Started only in case if InsertWasPerformed in deffered or not deffered way
        /// </summary>
        public void BeforeCommit()
        {          
            this.DoIndexing(this.tran,this.itbls);  //Do start indexing inside of commit            
        }

        /// <summary>
        ///  Started only in case if InsertWasPerformed in deffered or not deffered way
        /// </summary>
        public void AfterCommit()
        {
            //Trying start deffered indexer in parallel thread.
            if (defferedDocIds.Count > 0)
            {
                tran._transactionUnit.TransactionsCoordinator._engine.DeferredIndexer.Add(defferedDocIds);
                defferedDocIds.Clear();
                tran._transactionUnit.TransactionsCoordinator._engine.DeferredIndexer.StartDefferedIndexing();
            }
        }

        /// <summary>
        /// itbls and transaction must be supplied, to make it working from outside
        /// </summary>
        internal void DoIndexing(Transaction itran, Dictionary<string, ITS> xitbls)
        {

            byte[] btUdtStart = DateTime.UtcNow.Ticks.To_8_bytes_array_BigEndian();

            ITS its = null;

            byte[] kA = null;
            byte[] kZ = null;           
            byte[] newSrch = null;
            Row<string, byte[]> rWord = null;
            //Dictionary<string, WordInDocs> wds = new Dictionary<string, WordInDocs>();
            WordInDocs wd = null;

            uint iterBlockId = 0;
            int iterBlockLen = 0;
            int blockSize = 0;
            byte[] btBlock = null;
            Dictionary<uint, byte[]> block = new Dictionary<uint, byte[]>();
            byte[] btWah = null;
            byte[] tmp = null;
            byte[] val = null;
            WAH2 wah = null;
            


            foreach (var tbl in xitbls)
            {
                its = tbl.Value;
                if (its.srch == null)   //Can be instantiated in insert procedure, depending how we use indexer
                {
                    its.srch = itran.InsertTable<byte>(tbl.Key, 3, 0);
                    its.srch.ValuesLazyLoadingIsOn = false;
                }
                //Are instantiated only hear
                its.blocks = itran.InsertTable<byte>(tbl.Key, 10, 0);
                its.words = itran.InsertTable<byte>(tbl.Key, 20, 0);
                its.currentBlock = itran.Select<int, uint>(tbl.Key, 11).Value;
                its.numberInBlock = itran.Select<int, uint>(tbl.Key, 12).Value;                

                its.blocks.ValuesLazyLoadingIsOn = false;
                its.words.ValuesLazyLoadingIsOn = false;

                if (its.currentBlock == 0)
                {
                    its.numberInBlock = 0;
                    its.currentBlock = 1;
                }

                //Getting latest indexing time for that table
                var litRow = itran.Select<byte, byte[]>(tbl.Key, 4);
                byte[] lastIndexed = DateTime.MinValue.Ticks.To_8_bytes_array_BigEndian();
                if (litRow.Exists)
                    lastIndexed = litRow.Value;

                kA = lastIndexed.Concat(int.MinValue.To_4_bytes_array_BigEndian());
                kZ = DateTime.MaxValue.Ticks.To_8_bytes_array_BigEndian().Concat(int.MaxValue.To_4_bytes_array_BigEndian());

                //Key is word, Value.Item1 is documents list from which this word must be removed, Value.Item2 is documents List where word must be added
                Dictionary<string, Tuple<HashSet<int>, HashSet<int>, WordInDocs>> ds = new Dictionary<string, Tuple<HashSet<int>, HashSet<int>, WordInDocs>>();
                Tuple<HashSet<int>, HashSet<int>, WordInDocs> tpl = null;

                //Dictionary<string, byte[]> tmpWrds = new Dictionary<string, byte[]>(StringComparison.Ordinal);
                var tmpWrds = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);

                foreach (var docId in its.ChangedDocIds)
                {
                    //diff will return list of words to be removed and list of words to be added
                    newSrch = its.srch.Select<byte[], byte[]>(docId.To_4_bytes_array_BigEndian().Concat(new byte[] { 1 })).Value;

                    var diff = WordsDiff(
                        its.srch.Select<byte[], byte[]>(docId.To_4_bytes_array_BigEndian().Concat(new byte[] { 0 }), true).Value, //Current searchables 
                        newSrch //new
                        );

                    //Copying new searchables to current searchables
                    its.srch.ChangeKey<byte[]>(docId.To_4_bytes_array_BigEndian().Concat(new byte[] { 1 }), docId.To_4_bytes_array_BigEndian().Concat(new byte[] { 0 }));
                    //its.srch.Insert<byte[], byte[]>(docId.To_4_bytes_array_BigEndian().Concat(new byte[] { 0 }), newSrch);

                   

                    Action <string> createNew = (word) =>
                    {                    
                        if (!tmpWrds.ContainsKey(word))
                        {
                            rWord = its.words.Select<string, byte[]>(word, true);
                            wd = new WordInDocs();

                            if (rWord.Exists)
                            {
                                wd.BlockId = rWord.Value.Substring(0, 4).To_UInt32_BigEndian();
                                wd.NumberInBlock = rWord.Value.Substring(4, 4).To_UInt32_BigEndian();
                            }                        
                            else
                            {
                                its.numberInBlock++;
                            
                                if (its.numberInBlock > itran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.QuantityOfWordsInBlock)  //Quantity of words (WAHs) in block
                                {
                                    its.currentBlock++;
                                    its.numberInBlock = 1;
                                }

                                wd.BlockId = its.currentBlock;
                                wd.NumberInBlock = its.numberInBlock;
                                //Inserting new definition

                                

                                // its.words.Insert<string, byte[]>(word, wd.BlockId.To_4_bytes_array_BigEndian().Concat(wd.NumberInBlock.To_4_bytes_array_BigEndian()));
                                if (tmpWrds.Count < 100000)
                                    tmpWrds[word] = wd.BlockId.To_4_bytes_array_BigEndian().Concat(wd.NumberInBlock.To_4_bytes_array_BigEndian());
                                else
                                {
                                    // its.words.Insert<string, byte[]>(word, wd.BlockId.To_4_bytes_array_BigEndian().Concat(wd.NumberInBlock.To_4_bytes_array_BigEndian()));

                                    foreach (var tmpwrd in tmpWrds)
                                    {
                                        its.words.Insert<string, byte[]>(tmpwrd.Key, tmpwrd.Value);

                                    }
                                    tmpWrds.Clear();
                                }
                           
                            }
                            tpl = new Tuple<HashSet<int>, HashSet<int>, WordInDocs>(new HashSet<int>(), new HashSet<int>(), wd);
                            ds[word] = tpl;
                        }                        
                    };

                    //To be removed
                    foreach (var word in diff.Item1)
                    {
                        if (!ds.TryGetValue(word, out tpl))
                            createNew(word);

                        tpl.Item1.Add(docId);
                    }

                    //To be added
                    foreach (var word in diff.Item2)
                    {
                        if (!ds.TryGetValue(word, out tpl))
                            createNew(word);

                        tpl.Item2.Add(docId);
                    }
                }//eo foreach new searchables, end of document itteration              

                foreach (var tmpwrd in tmpWrds)
                {
                    its.words.Insert<string, byte[]>(tmpwrd.Key, tmpwrd.Value);

                }
                tmpWrds.Clear();


                #region "S1"
                //Inserting WAH blocks
                //Going through the list of collected words order by blockID, fill blocks and save them                  
                block.Clear();
                iterBlockId = 0;

                foreach (var wd1 in ds.OrderBy(r => r.Value.Item3.BlockId))
                {
                    //reading block if it's not loaded
                    if (wd1.Value.Item3.BlockId != iterBlockId)
                    {
                        if (iterBlockId > 0)
                        {
                            //We must save current datablock
                            if (block.Count() > 0)
                            {

                                btBlock = block.Encode_DICT_PROTO_UINT_BYTEARRAY(Compression.eCompressionMethod.Gzip);

                                if ((btBlock.Length + 4) < itran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.MinimalBlockReservInBytes)    //Minimal reserv
                                {
                                    tmp = new byte[itran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.MinimalBlockReservInBytes];
                                    tmp.CopyInside(0, btBlock.Length.To_4_bytes_array_BigEndian());
                                    tmp.CopyInside(4, btBlock);
                                }
                                else if ((btBlock.Length + 4) > iterBlockLen)
                                {
                                    //Doubling reserve
                                    tmp = new byte[btBlock.Length * 2];
                                    tmp.CopyInside(0, btBlock.Length.To_4_bytes_array_BigEndian());
                                    tmp.CopyInside(4, btBlock);
                                }
                                else
                                {
                                    //Filling existing space
                                    tmp = new byte[btBlock.Length + 4];
                                    tmp.CopyInside(0, btBlock.Length.To_4_bytes_array_BigEndian());
                                    tmp.CopyInside(4, btBlock);
                                }

                                //Saving into DB                                   
                                its.blocks.Insert<uint, byte[]>(iterBlockId, tmp);
                            }

                            block.Clear();
                        }

                        val = its.blocks.Select<uint, byte[]>(wd1.Value.Item3.BlockId).Value;
                        iterBlockId = wd1.Value.Item3.BlockId;
                        iterBlockLen = val == null ? 0 : val.Length;

                        if (val != null)
                        {
                            blockSize = val.Substring(0, 4).To_Int32_BigEndian();
                            if (blockSize > 0)
                            {
                                btBlock = val.Substring(4, blockSize);
                                block.Clear();
                                btBlock.Decode_DICT_PROTO_UINT_BYTEARRAY(block, Compression.eCompressionMethod.Gzip);
                            }
                            else
                                block.Clear();
                        }
                        else
                            block.Clear();
                    }

                    //Getting from Block 
                    if (block.TryGetValue((uint)wd1.Value.Item3.NumberInBlock, out btWah))
                    {
                        wah = new WAH2(btWah);
                    }
                    else
                        wah = new WAH2(null);

                    //Adding documents
                    foreach (var dId in wd1.Value.Item2)
                        wah.Add(dId, true);

                    //Removing documents
                    foreach (var dId in wd1.Value.Item1)
                        wah.Add(dId, false);

                    block[wd1.Value.Item3.NumberInBlock] = wah.GetCompressedByteArray();

                }//eo foreach wds


                //Saving last element
                //saving current block
                if (block.Count() > 0)
                {
                    //!!!!!!!!!!! Remake it for smoothing storage 
                    btBlock = block.Encode_DICT_PROTO_UINT_BYTEARRAY(Compression.eCompressionMethod.Gzip);

                    if ((btBlock.Length + 4) < itran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.MinimalBlockReservInBytes)    //Minimal reserve
                    {
                        tmp = new byte[itran._transactionUnit.TransactionsCoordinator._engine.Configuration.TextSearchConfig.MinimalBlockReservInBytes];
                        tmp.CopyInside(0, btBlock.Length.To_4_bytes_array_BigEndian());
                        tmp.CopyInside(4, btBlock);
                    }
                    else if ((btBlock.Length + 4) > iterBlockLen)
                    {
                        //Doubling reserve
                        tmp = new byte[btBlock.Length * 2];
                        tmp.CopyInside(0, btBlock.Length.To_4_bytes_array_BigEndian());
                        tmp.CopyInside(4, btBlock);
                    }
                    else
                    {
                        //Filling existing space
                        tmp = new byte[btBlock.Length + 4];
                        tmp.CopyInside(0, btBlock.Length.To_4_bytes_array_BigEndian());
                        tmp.CopyInside(4, btBlock);
                    }

                    //Saving into DB          
                    its.blocks.Insert<uint, byte[]>(iterBlockId, tmp);
                }

                block.Clear();
                #endregion

                itran.Insert<int, uint>(tbl.Key, 11, its.currentBlock);
                itran.Insert<int, uint>(tbl.Key, 12, its.numberInBlock);

                //Setting last indexing time
                itran.Insert<byte, byte[]>(tbl.Key, 4, btUdtStart);

            }//eo foreach tablesToIndex            
        }

        /// <summary>
        /// This function accepts old value of searchables for one document and new value,
        /// decides what must be Removed (par1) and what should be Added (par2)
        /// </summary>
        /// <param name="oldtext"></param>
        /// <param name="newtext"></param>
        /// <returns>List 2remove, List 2add</returns>
        Tuple<HashSet<string>, HashSet<string>> WordsDiff(byte[] oldtext, byte[] newtext)
        {
            HashSet<string> toRemove = new HashSet<string>();
            HashSet<string> toAdd = new HashSet<string>();

            //Debug.WriteLine(word);

            HashSet<string> nt = GetSearchablesFromByteArray_AsHashSet(newtext);
            HashSet<string> ot = GetSearchablesFromByteArray_AsHashSet(oldtext);

            foreach (var word in nt)
            {
                if (!ot.Contains(word))
                    toAdd.Add(word);
            }

            foreach (var word in ot)
            {
                if (!nt.Contains(word))
                    toRemove.Add(word);
            }


            return new Tuple<HashSet<string>, HashSet<string>>(toRemove, toAdd);
        }

        #region "Converters"
        /// <summary>
        /// Converter from searchbales to byte[]
        /// </summary>
        /// <param name="searchables"></param>
        /// <returns></returns>
        byte[] GetByteArrayFromSearchbles(string searchables)
        {
            return searchables.To_UTF8Bytes().GZip_Compress();
        }

        /// <summary>
        /// Converter from byte[] searchables.
        /// </summary>
        /// <param name="searchables"></param>
        /// <returns></returns>
        string GetSearchablesFromByteArray(byte[] searchables)
        {
            if (searchables == null)
                return String.Empty;
            return searchables.GZip_Decompress().ToUTF8String();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="searchables"></param>
        /// <returns></returns>
        HashSet<string> GetSearchablesFromByteArray_AsHashSet(byte[] searchables)
        {
            HashSet<string> res = new HashSet<string>(StringComparer.Ordinal);

            string r = GetSearchablesFromByteArray(searchables);
            if (r == String.Empty)
                return res;
            foreach (var word in r.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                res.Add(word);
            return res;
        }

        #endregion

        /// <summary>
        /// 
        /// </summary>        
        class WordDefinition
        {
            public uint CountInDocu = 0;
        }

        /// <summary>
        /// Returns null in case of notfound anything or what ever
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        SortedDictionary<string, WordDefinition> GetWordsDefinitionFromText(string text, TextSearchStorageOptions opt)
        {
            SortedDictionary<string, WordDefinition> wordsCounter = new SortedDictionary<string, WordDefinition>();

            try
            {
                if (String.IsNullOrEmpty(text))
                    return wordsCounter;

                StringBuilder sb = new StringBuilder();
                string word = "";
                WordDefinition wordDefinition = null;

                //Support for previous versions without FullTextOnly
                if (!opt.FullTextOnly && opt.SearchWordMinimalLength == 0)
                {
                    opt.FullTextOnly = true;
                    opt.SearchWordMinimalLength = 2;
                }


                Action processWord = () =>
                {
                    //We take all words, so we can later find even by email address jj@gmx.net ... we will need jj and gmx.net
                    if (sb.Length > 0 && sb.Length >= opt.SearchWordMinimalLength)
                    {
                        word = sb.ToString().ToLower();

                        List<string> wrds = new List<string>();
                        wrds.Add(word);
                        int i = 1;

                        if (!opt.FullTextOnly)   //If equals to 0, we store only words for full text search
                        {
                            while (word.Length - i >= opt.SearchWordMinimalLength)
                            {
                                wrds.Add(word.Substring(i));
                                i++;
                            }
                        }

                        // System.Diagnostics.Debug.WriteLine("--------------");
                        foreach (var w in wrds)
                        {
                            //System.Diagnostics.Debug.WriteLine(w);
                            if (wordsCounter.TryGetValue(w, out wordDefinition))
                            {
                                wordDefinition.CountInDocu++;
                            }
                            else
                            {
                                wordDefinition = new WordDefinition() { CountInDocu = 1 };
                                wordsCounter[w] = wordDefinition;
                            }
                        }

                    }

                    if (sb.Length > 0)
                        sb.Remove(0, sb.Length);
                    //sb.Clear();
                };

                int wordLen = 0;
                int maximalWordLengthBeforeSplit = 50;

                foreach (var c in text)
                {
                    //No words reviews (must be checked in outer systems)
                    if (c != ' ')
                    {
                        sb.Append(c);
                        wordLen++;

                        if (wordLen >= maximalWordLengthBeforeSplit)
                        {
                            //Processing ready word
                            processWord();
                            wordLen = 0;
                        }
                    }
                    else
                    {
                        //Processing ready word
                        processWord();
                        wordLen = 0;
                    }

                    //---REVIEW ON START
                    //if (c == '-' || c == '@')   //Complex names or email address inside
                    //    continue;

                    //if (Char.IsLetterOrDigit(c) || Char.IsSymbol(c))
                    //{
                    //    sb.Append(c);
                    //    wordLen++;

                    //    if (wordLen >= maximalWordLengthBeforeSplit)
                    //    {
                    //        //Processing ready word
                    //        processWord();
                    //        wordLen = 0;
                    //    }
                    //}
                    //else
                    //{
                    //    //Processing ready word
                    //    processWord();
                    //    wordLen = 0;
                    //}
                    //---REVIEW ON STOP
                }

                //Processing last word
                processWord();

                //if (wordsCounter.Count() > 0)
                //    return wordsCounter;
            }
            catch (System.Exception ex)
            {

            }

            return wordsCounter;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="searchKeywords"></param>
        /// <returns></returns>
        private HashSet<string> PrepareSearchKeyWords(string searchKeywords)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                HashSet<string> words = new HashSet<string>();
                string word = String.Empty;

                //if (!FullTextOnly && this.SearchWordMinimalLength == 0)
                //{
                //    FullTextOnly = true;
                //    this.SearchWordMinimalLength = 2;
                //}

                Action processWord = () =>
                {
                    if (sb.Length > 0 && sb.Length >= 2)
                    {
                        word = sb.ToString().ToLower();
                        if (!words.Contains(word))
                            words.Add(word);
                    }

                    if (sb.Length > 0)
                        sb.Remove(0, sb.Length);
                    //sb.Clear();
                };


                //NO REVIEW
                foreach (var c in searchKeywords)
                {
                    if (c == ' ')
                        processWord();
                    else 
                        sb.Append(c);
                }


                //WITH REVIEW START
                //foreach (var c in searchKeywords)
                //{
                //    if (c == '-' || c == '@')   //Complex names or email address inside
                //        continue;

                //    if (Char.IsLetterOrDigit(c) || Char.IsSymbol(c))
                //    {
                //        sb.Append(c);
                //    }
                //    else
                //    {
                //        processWord();
                //    }
                //}
                //WITH REVIEW STOP

                //Handling last word
                processWord();

                return words;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }


    }//eoc
}
